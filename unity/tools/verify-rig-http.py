"""Resumable, real-local-Blender HTTP smoke; use only the loopback fixture."""
import json
from pathlib import Path
import time
from urllib.request import Request, urlopen
import uuid

ROOT = Path(__file__).resolve().parents[1] / 'withme_frontend'
checkpoint = ROOT / 'Temp/batch/rig-http-smoke.json'
state = json.loads(checkpoint.read_text()) if checkpoint.exists() else {'key': str(uuid.uuid4())}
origin = 'http://127.0.0.1:8767'

def request(path, body=None, key=None, binary=False):
    headers = {'Authorization': 'Bearer demo-access', 'Content-Type': 'application/json'}
    if key: headers['Idempotency-Key'] = key
    with urlopen(Request(origin + path, None if body is None else json.dumps(body).encode(), headers), timeout=10) as response:
        data = response.read()
        return data if binary else json.loads(data)

def save():
    checkpoint.parent.mkdir(parents=True, exist_ok=True); checkpoint.write_text(json.dumps(state))

def wait(job, timeout=150):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = request('/jobs/' + job)
        if result['status'] == 'failed': raise RuntimeError(result.get('errorCode'))
        if result['status'] == 'succeeded': return result['result']
        time.sleep(.5)
    raise TimeoutError('Local job still running; rerun to resume polling')

save()
if 'referenceJob' not in state:
    state['referenceJob'] = request('/pets/22222222-2222-4222-8222-222222222222/reference-views', {'sourceMediaIds': ['fixture-photo']}, state['key'])['jobId']; save()
reference = wait(state['referenceJob'])
if 'modelJob' not in state:
    state['modelJob'] = request('/reference-views/' + reference['referenceViewId'] + '/approve', {}, state['key'])['jobId']; save()
model = wait(state['modelJob']); model_id = model['characterModelId']
assert '/standard/prepared/' in model['glbUrl']
joints = request('/character-models/' + model_id + '/joints')
assert joints['space'] == 'normalized' and len(joints['joints']) == 10
accepted = request('/character-models/' + model_id + '/rig', joints)
assert accepted == request('/character-models/' + model_id + '/rig', joints)
state['rigJob'] = accepted['jobId']; save()
result = wait(state['rigJob'])
assert not result['fixtureOnly'] and result['correctionApplied']
assert not result['quality']['accepted']
data = request(result['riggedGlbUrl'].removeprefix(origin), binary=True)
assert data[:4] == b'glTF'
state['verified'] = {'bytes': len(data), 'qualityAccepted': False, 'realLocalBlender': True, 'deduplication': True}
save(); print(json.dumps(state['verified']))
