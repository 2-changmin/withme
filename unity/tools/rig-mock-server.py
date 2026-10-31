"""Loopback character fixture + canonical joints + optional real local Blender.

No upstream calls or production credentials. The existing #10 server remains
independent. Without --blender only the response flow is simulated, explicitly.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys
import threading
import time
import uuid
from http.server import ThreadingHTTPServer
from urllib.parse import urlsplit

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('generation_fixture', HERE / 'generation-mock-server.py')
base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / 'blender'))
from dogrig import RigError
from dogrig.joint_contract import validate
from worker_adapter import run_rig

STANDARD = base.ROOT / 'Assets/StreamingAssets/Validation/StandardRig'
ARTIFACTS = base.ROOT / 'Temp/rig-mock-artifacts'


class Handler(base.Handler):
    def reply(self, status, body):
        if isinstance(body, dict) and body.get('type') == 'meshy_generate' and body.get('result'):
            body['result']['glbUrl'] = self.origin() + f'/standard/prepared/{self.server.model}.glb'
        super().reply(status, body)

    def authorized(self):
        return self.headers.get('Authorization') == 'Bearer demo-access'

    def known_model(self, model):
        return any(j['type'] == 'meshy_generate' and j['modelId'] == model for j in base.state['jobs'].values())

    def file(self, root, relative):
        path = (root / relative).resolve()
        if not path.is_relative_to(root.resolve()) or not path.is_file(): return self.error(404, 'NOT_FOUND')
        data = path.read_bytes(); self.send_response(200)
        self.send_header('Content-Type', 'model/gltf-binary' if path.suffix == '.glb' else 'application/json')
        self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data)

    def do_GET(self):
        path = urlsplit(self.path).path
        if path.startswith('/standard/'): return self.file(STANDARD, path.removeprefix('/standard/'))
        if path.startswith('/rig-artifacts/'): return self.file(ARTIFACTS, path.removeprefix('/rig-artifacts/'))
        match = re.fullmatch(r'/character-models/([a-zA-Z0-9_-]+)/joints', path)
        if match:
            if not self.authorized(): return self.error(401, 'UNAUTHORIZED')
            with base.lock:
                if not self.known_model(match[1]): return self.error(404, 'NOT_FOUND')
                if time.time() - self.server.started < self.server.joints_delay: return self.error(409, 'NOT_READY')
            return self.reply(200, json.loads((STANDARD / (self.server.model + '.joints.json')).read_text()))
        if path.startswith('/jobs/'):
            if not self.authorized(): return self.error(401, 'UNAUTHORIZED')
            with base.lock:
                job = base.state['jobs'].get(path.split('/')[-1])
                if job and job['type'] == 'rig':
                    result = job.get('result'); status = job['status']
                    return self.reply(200, {'id': job['id'], 'type': 'rig', 'status': status,
                        'progress': 100 if status in ('succeeded', 'failed') else 25,
                        'result': result, 'errorCode': job.get('errorCode')})
        return super().do_GET()

    def do_POST(self):
        match = re.fullmatch(r'/character-models/([a-zA-Z0-9_-]+)/rig', urlsplit(self.path).path)
        if not match: return super().do_POST()
        if not self.authorized(): return self.error(401, 'UNAUTHORIZED')
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if length < 1 or length > 8192: return self.error(422, 'VALIDATION_FAILED')
            joints = validate(json.loads(self.rfile.read(length)))
        except (ValueError, TypeError, RigError): return self.error(422, 'VALIDATION_FAILED')
        with base.lock:
            if not self.known_model(match[1]): return self.error(404, 'NOT_FOUND')
            identity = 'rig:' + match[1] + ':' + hashlib.sha256(json.dumps(joints, sort_keys=True).encode()).hexdigest()
            previous = base.state['jobs'].get(base.state['keys'].get(identity))
            if previous and previous.get('status') != 'failed': return self.reply(202, {'jobId': previous['id']})
            job_id = str(uuid.uuid4())
            base.state['jobs'][job_id] = {'id': job_id, 'type': 'rig', 'status': 'queued', 'modelId': match[1], 'started': time.time(), 'joints': joints, 'fixtureModel': self.server.model}
            base.state['keys'][identity] = job_id; base.save()
            origin = self.origin(); model = self.server.model; executable = self.server.blender
            threading.Thread(target=run_job, args=(job_id, match[1], joints, origin, model, executable, self.server.rig_fail), daemon=True).start()
            return self.reply(202, {'jobId': job_id})


def run_job(job_id, model_id, joints, origin, model, executable, fail=False):
    with base.lock: base.state['jobs'][job_id]['status'] = 'running'; base.save()
    try:
        if fail: raise RuntimeError('Injected local test failure')
        if executable:
            directory = ARTIFACTS / job_id; directory.mkdir(parents=True, exist_ok=True)
            input_file = directory / 'joints.json'; input_file.write_text(json.dumps(joints))
            outcome = run_rig(executable, STANDARD / f'prepared/{model}.glb', input_file, directory / 'work')
            url = origin + f'/rig-artifacts/{job_id}/work/rigged.glb'
            quality = outcome.report['quality']
        else:
            time.sleep(1)
            url = origin + f'/standard/{model}.glb'
            quality = json.loads((STANDARD / (model + '.quality.json')).read_text())['quality']
        result = {'characterModelId': model_id, 'riggedGlbUrl': url, 'skeletonVersion': 1,
                  'quality': quality, 'fixtureOnly': not bool(executable), 'correctionApplied': bool(executable)}
        with base.lock: base.state['jobs'][job_id].update(status='succeeded', result=result); base.save()
    except Exception as error:
        with base.lock: base.state['jobs'][job_id].update(status='failed', errorCode=getattr(error, 'code', 'RIG_FAILED')); base.save()


def resume(server):
    # The persisted canonical input is enough to resume; completed output is
    # reused by worker_adapter instead of launching the same Blender run again.
    origin = f'http://127.0.0.1:{server.server_port}'
    for job in list(base.state['jobs'].values()):
        if job['type'] != 'rig' or job.get('status') not in ('queued', 'running'): continue
        if 'joints' not in job or 'fixtureModel' not in job:
            job.update(status='failed', errorCode='INTERRUPTED'); base.save(); continue
        threading.Thread(target=run_job, args=(job['id'], job['modelId'], job['joints'], origin, job['fixtureModel'], server.blender, server.rig_fail), daemon=True).start()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=8767)
    parser.add_argument('--model', choices=['m1_golden', 'm2_white', 'm3_cream'], default='m1_golden')
    parser.add_argument('--blender', help='optional real local Blender executable')
    parser.add_argument('--duration', type=float, default=2)
    parser.add_argument('--joints-delay', type=float, default=0, help='local 409 readiness test')
    parser.add_argument('--rig-fail', action='store_true', help='local failed-job test')
    args = parser.parse_args()
    if args.blender and not Path(args.blender).is_file(): parser.error('Blender executable not found')
    base.STATE = base.ROOT / f'Temp/rig-mock-state-{args.port}.json'
    base.state = json.loads(base.STATE.read_text()) if base.STATE.exists() else {'jobs': {}, 'keys': {}, 'retries': {}, 'approved': {}}
    server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    server.model = args.model; server.blender = args.blender; server.duration = args.duration; server.fixture = 'golden'; server.fail = None
    server.started = time.time(); server.joints_delay = max(0, args.joints_delay); server.rig_fail = args.rig_fail
    resume(server)
    print(f'Rig fixture on 127.0.0.1:{args.port}; real local Blender={bool(args.blender)}; upstream calls=0', flush=True)
    server.serve_forever()
