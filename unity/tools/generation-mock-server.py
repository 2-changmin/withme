"""Local #10 fixture. No upstream calls; accepts only the fake demo credential."""
import argparse
import json
import mimetypes
from pathlib import Path
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1] / 'withme_frontend'
ASSETS = ROOT / 'Assets/StreamingAssets/Validation/Generation'
STATE = ROOT / 'Temp/generation-mock-state.json'
lock = threading.RLock()
state = json.loads(STATE.read_text(encoding='utf-8')) if STATE.exists() else {'jobs': {}, 'keys': {}, 'retries': {}, 'approved': {}}

def save():
    STATE.parent.mkdir(parents=True, exist_ok=True)
    tmp = STATE.with_suffix('.tmp')
    tmp.write_text(json.dumps(state), encoding='utf-8')
    tmp.replace(STATE)

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass  # Do not log authorization or asset URLs.

    def reply(self, status, body):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def error(self, status, code):
        self.reply(status, {'error': {'code': code, 'message': 'Local fixture'}})

    def origin(self):
        return f'http://127.0.0.1:{self.server.server_port}'

    def do_GET(self):
        path = urlsplit(self.path).path
        if path == '/health':
            return self.reply(200, {'fixture': True, 'externalCalls': 0})
        if path.startswith('/assets/'):
            relative = path.removeprefix('/assets/')
            file = (ASSETS / relative).resolve()
            if not file.is_relative_to(ASSETS.resolve()) or not file.is_file():
                return self.error(404, 'NOT_FOUND')
            data = file.read_bytes()
            self.send_response(200)
            self.send_header('Content-Type', mimetypes.guess_type(file.name)[0] or 'application/octet-stream')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            return
        if self.headers.get('Authorization') != 'Bearer demo-access':
            return self.error(401, 'UNAUTHORIZED')
        if path.startswith('/media/'):
            return self.reply(200, {'id': path.split('/')[-1], 'status': 'ready', 'url': self.origin() + f'/assets/{self.server.fixture}/front.png'})
        with lock:
            job = state['jobs'].get(path.removeprefix('/jobs/')) if path.startswith('/jobs/') else None
            if not job:
                return self.error(404, 'NOT_FOUND')
            elapsed = time.time() - job['started']
            status = 'queued' if elapsed < .7 else 'running' if elapsed < self.server.duration else 'succeeded'
            progress = min(100, int(elapsed / self.server.duration * 100))
            result = None
            if status == 'succeeded':
                if job['type'] == 'reference_views':
                    result = {'referenceViewId': job['referenceId'], 'views': [self.origin() + f'/assets/{self.server.fixture}/{v}.png' for v in ['side', 'front', 'back', 'other']], 'problems': []}
                else:
                    result = {'characterModelId': job['modelId'], 'glbUrl': self.origin() + '/assets/Dog.glb'}
            if status == 'succeeded' and self.server.fail:
                status, result = 'failed', None
            self.reply(200, {'id': job['id'], 'type': job['type'], 'status': status, 'progress': progress, 'result': result, 'errorCode': self.server.fail if status == 'failed' else None})

    def do_POST(self):
        if self.headers.get('Authorization') != 'Bearer demo-access':
            return self.error(401, 'UNAUTHORIZED')
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if length > 8192:
                return self.error(422, 'VALIDATION_FAILED')
            body = json.loads(self.rfile.read(length) or b'{}')
        except (ValueError, json.JSONDecodeError):
            return self.error(422, 'VALIDATION_FAILED')
        parts = urlsplit(self.path).path.strip('/').split('/')
        key = self.headers.get('Idempotency-Key')
        with lock:
            if len(parts) == 3 and parts[0] == 'pets' and parts[2] == 'reference-views':
                ids = body.get('sourceMediaIds', [])
                if not 1 <= len(ids) <= 4 or not key:
                    return self.error(422, 'VALIDATION_FAILED')
                kind, identity = 'reference_views', 'create:' + parts[1] + ':' + key
            elif len(parts) == 3 and parts[0] == 'reference-views' and parts[2] in ('retry', 'approve'):
                prior = next((j for j in state['jobs'].values() if j.get('referenceId') == parts[1]), None)
                if not prior:
                    return self.error(404, 'NOT_FOUND')
                if time.time() - prior['started'] < self.server.duration:
                    return self.error(409, 'CONFLICT')
                kind = 'reference_views' if parts[2] == 'retry' else 'meshy_generate'
                identity = parts[2] + ':' + parts[1]  # same reference/operation always returns the same job
            else:
                return self.error(404, 'NOT_FOUND')
            if identity in state['keys']:
                return self.reply(202, {'jobId': state['keys'][identity]})
            if parts[0] == 'reference-views' and parts[2] == 'retry':
                count = state['retries'].get(parts[1], 0)
                if count >= 3:
                    return self.error(429, 'LIMIT_EXCEEDED')
            job_id = str(uuid.uuid4())
            reference = str(uuid.uuid4()) if kind == 'reference_views' else parts[1]
            state['jobs'][job_id] = {'id': job_id, 'type': kind, 'started': time.time(), 'referenceId': reference, 'modelId': str(uuid.uuid4())}
            if parts[0] == 'reference-views' and parts[2] == 'retry':
                state['retries'][reference] = state['retries'].get(parts[1], 0) + 1
            state['keys'][identity] = job_id
            save()
            self.reply(202, {'jobId': job_id})

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=8765)
    parser.add_argument('--duration', type=float, default=6)
    parser.add_argument('--fixture', choices=['golden', 'maltese'], default='golden')
    parser.add_argument('--fail', choices=['UPSTREAM_FAILED', 'LIMIT_EXCEEDED'])
    args = parser.parse_args()
    if args.port != 8765:
        STATE = ROOT / f'Temp/generation-mock-state-{args.port}.json'
        state = json.loads(STATE.read_text(encoding='utf-8')) if STATE.exists() else {'jobs': {}, 'keys': {}, 'retries': {}, 'approved': {}}
    server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    server.duration, server.fixture, server.fail = max(1, args.duration), args.fixture, args.fail
    print(f'Withme fixture listening on 127.0.0.1:{args.port}; no upstream calls', flush=True)
    server.serve_forever()
