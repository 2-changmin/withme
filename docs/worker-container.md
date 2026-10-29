# 작업자 컨테이너 (#28)

작업자는 Python + Blender 4.2 헤드리스 + FFmpeg를 한 이미지(`backend/Dockerfile.worker`)에 담아 실행한다.
처리 함수는 `run_blender("<스크립트>.py", ...)`로 `blender/` 폴더의 스크립트를 실행하면 된다.

## 로컬에서 실행

```bash
# DB + 작업자 (기본 docker compose up -d는 DB만 띄운다)
docker compose --profile worker up -d --build worker
docker compose logs -f worker
```

- `backend/.env`를 읽지만 `DATABASE_URL`, `BLENDER_PATH`, `BLENDER_SCRIPTS_DIR`은 compose에서 컨테이너 값으로 고정한다.
- `backend/.media`를 같이 마운트하므로, 로컬에서 띄운 API가 저장한 임시 미디어를 작업자도 읽는다 (#30 버킷 전까지).
- `MOCK_GEMINI`·`MOCK_MESHY` 기본값은 `true`라서 켜기만 해서는 비용이 들지 않는다.

**Apple Silicon**: Blender 공식 Linux 빌드는 x64만 있어서 `linux/amd64` 에뮬레이션으로 돈다. 동작은 하지만 Blender 작업이 몇 배 느리다. 배포 서버(#29)는 x64를 쓴다.

## 동작 확인

```bash
# Blender 헤드리스로 큐브 GLB 내보내기
docker run --rm --platform linux/amd64 withme-worker \
  blender -b --factory-startup -P /srv/blender/check.py -- /tmp/cube.glb
# 마지막 줄: CHECK_OK 4.2.0 <크기>

docker run --rm --platform linux/amd64 withme-worker ffmpeg -version
```

`backend/tests/test_blender.py`는 Blender가 있는 환경(컨테이너, Blender를 설치한 PC)에서만 실행되고, 없으면 건너뛴다.

## 임시 방안: 팀원 PC에서 작업자 실행

컨테이너를 쓸 수 없을 때(서버 배포 전, 에뮬레이션이 너무 느릴 때)는 Blender를 설치한 PC에서 작업자를 직접 띄운다.
큐는 DB 하나를 같이 쓰고 작업마다 lease를 잡으므로, 작업자를 여러 대 띄워도 같은 작업을 두 번 처리하지 않는다.

1. [Blender 4.2 LTS](https://www.blender.org/download/lts/4-2/)와 FFmpeg 설치 (macOS: `brew install ffmpeg`)
2. `backend/.env` 설정
   ```
   DATABASE_URL=<공용 DB 주소, 로컬 테스트면 기본값>
   WORKER_ID=worker-<이름>          # 로그에서 누가 처리했는지 구분
   BLENDER_PATH=/Applications/Blender.app/Contents/MacOS/Blender   # Windows: C:\Program Files\Blender Foundation\Blender 4.2\blender.exe
   BLENDER_SCRIPTS_DIR=../blender
   ```
3. 확인 후 실행
   ```bash
   cd backend && source .venv/bin/activate
   pytest tests/test_blender.py      # passed가 나와야 한다 (skipped면 BLENDER_PATH 확인)
   python -m app.worker.main
   ```

PC를 끄거나 작업자를 멈추면 처리 중이던 작업은 lease(`WORKER_LEASE_SECONDS`, 기본 300초)가 끝난 뒤 다른 작업자가 다시 가져간다.
