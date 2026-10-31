"""Local HTTP regression tests. No upstream services or paid generation."""
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

spec = importlib.util.spec_from_file_location('rig_mock', Path(__file__).with_name('rig-mock-server.py'))
mock = importlib.util.module_from_spec(spec); spec.loader.exec_module(mock)


class RigHttpTests(unittest.TestCase):
    def setUp(self):
        parent = mock.base.ROOT / 'Temp/batch/tests'; parent.mkdir(parents=True, exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(dir=parent)
        mock.base.STATE = Path(self.directory.name) / 'state.json'
        mock.base.state = {'jobs': {'source': {'id': 'source', 'type': 'meshy_generate', 'modelId': 'model', 'started': 0}}, 'keys': {}, 'retries': {}, 'approved': {}}
        self.server = mock.ThreadingHTTPServer(('127.0.0.1', 0), mock.Handler)
        self.server.model = 'm1_golden'; self.server.blender = None
        self.server.started = time.time(); self.server.joints_delay = 0; self.server.rig_fail = False
        self.server.duration = 1; self.server.fixture = 'golden'; self.server.fail = None
        self.origin = f'http://127.0.0.1:{self.server.server_port}'
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True); self.thread.start()

    def tearDown(self):
        self.server.shutdown(); self.server.server_close(); self.thread.join()
        self.directory.cleanup()

    def request(self, path, body=None, auth=True):
        headers = {'Authorization': 'Bearer demo-access'} if auth else {}
        if body is not None: headers['Content-Type'] = 'application/json'
        request = Request(self.origin + path, json.dumps(body).encode() if body is not None else None, headers)
        try:
            with urlopen(request, timeout=4) as response:
                data = response.read()
                return response.status, json.loads(data) if response.headers.get_content_type() == 'application/json' else data
        except HTTPError as error: return error.code, json.loads(error.read())

    def wait(self, job):
        end = time.monotonic() + 5
        while time.monotonic() < end:
            _, result = self.request('/jobs/' + job)
            if result['status'] in ('succeeded', 'failed'): return result
            time.sleep(.03)
        self.fail('local job did not finish')

    def test_auth_validation_and_readiness(self):
        self.assertEqual(401, self.request('/character-models/model/joints', auth=False)[0])
        self.assertEqual(401, self.request('/character-models/model/rig', {}, auth=False)[0])
        self.assertEqual(422, self.request('/character-models/model/rig', {})[0])
        self.assertEqual(422, self.request('/character-models/model/rig', [1])[0])
        self.assertEqual(404, self.request('/character-models/missing/joints')[0])
        self.server.joints_delay = 100
        self.assertEqual(409, self.request('/character-models/model/joints')[0])

    def test_prepared_mesh_and_deduplicated_review_result(self):
        _, generation = self.request('/jobs/source')
        code, glb = self.request(generation['result']['glbUrl'].removeprefix(self.origin))
        self.assertEqual((200, b'glTF'), (code, glb[:4]))
        _, joints = self.request('/character-models/model/joints')
        self.assertEqual(('normalized', 1, 10), (joints['space'], joints['skeletonVersion'], len(joints['joints'])))
        _, first = self.request('/character-models/model/rig', joints)
        self.assertEqual(first, self.request('/character-models/model/rig', joints)[1])
        job = self.wait(first['jobId']); self.assertEqual('succeeded', job['status'])
        result = job['result']
        self.assertTrue(result['fixtureOnly']); self.assertFalse(result['correctionApplied'])
        self.assertFalse(result['quality']['accepted'])
        self.assertEqual(b'glTF', self.request(result['riggedGlbUrl'].removeprefix(self.origin))[1][:4])

    def test_failure_retry_and_interruption_recovery(self):
        _, joints = self.request('/character-models/model/joints')
        self.server.rig_fail = True
        _, first = self.request('/character-models/model/rig', joints)
        self.assertEqual('failed', self.wait(first['jobId'])['status'])
        self.server.rig_fail = False
        _, second = self.request('/character-models/model/rig', joints)
        self.assertNotEqual(first, second)
        self.wait(second['jobId'])
        # Emulate a crash checkpoint, then use the same recovery used at startup.
        with mock.base.lock:
            job = mock.base.state['jobs'][second['jobId']]
            job['status'] = 'running'; job.pop('result'); mock.base.save()
        mock.resume(self.server)
        self.assertEqual('succeeded', self.wait(second['jobId'])['status'])


if __name__ == '__main__': unittest.main()
