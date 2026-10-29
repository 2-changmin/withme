# 서버 배포 (#29)

학교 클라우드 서버 1대에 API와 작업자를 Docker로 띄운다. DB·파일은 Supabase(서울)를 쓴다.

| 항목 | 값 |
|---|---|
| 앱이 부르는 주소 | `https://gomagochi-jeahyun.apps.dauai.org` (HTTPS는 학교 프록시가 처리 → 서버 80번) |
| 서버 | Ubuntu 24.04, x86_64 2코어, 메모리 4GB |
| 접속 | `ssh gomagochi` (학교에서 받은 config·키를 `~/.ssh`에 둔다. 박재현) |
| 코드 위치 | 서버의 `~/withme` |

## 설정: `backend/.env.prod` (서버에만)

저장소에 올리지 않는다 (`.gitignore`의 `.env.*`). 권한은 `600`.

```
ENV=prod                      # local이 아니면 X-Dev-User 우회와 local 저장소가 꺼진다
DATABASE_URL=<Supabase Session pooler 주소>
SUPABASE_URL=https://<프로젝트>.supabase.co
SUPABASE_SERVICE_ROLE_KEY=<service_role 키>
STORAGE_BACKEND=supabase
MOCK_CHARACTER=true           # #23 연결 전까지
MOCK_GEMINI=true              # 실제 호출 때만 false + GEMINI_API_KEY
MOCK_MESHY=true               # 실제 호출 때만 false + MESHY_API_KEY
WORKER_ID=worker-server
```

Gemini·Meshy 키는 이 파일에만 넣는다. 앱(Unity)과 저장소에는 절대 넣지 않는다.

## 배포·갱신

```bash
ssh gomagochi
cd ~/withme && git pull
docker compose -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.prod.yml ps          # api가 healthy인지
curl -s https://gomagochi-jeahyun.apps.dauai.org/health   # {"status":"ok","env":"prod"}
```

## 로그

```bash
docker compose -f docker-compose.prod.yml logs -f --tail 100 api worker
```

컨테이너마다 20MB × 5개까지 보관한다. 처리하지 못한 오류는 API가 `서버 오류`로만 답하고 원인은 이 로그에 남는다.

## 마이그레이션

새 마이그레이션은 PR이 병합된 뒤 Supabase DB에 직접 적용한다. 0001~0003은 2026-10-29에 적용했다.
적용한 뒤 팀에 알린다.
