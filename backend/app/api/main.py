"""API 서버 진입점: `uvicorn app.api.main:app --reload`"""
from fastapi import FastAPI

from app.api.routes import character, health, items, jobs, media, pets
from app.errors import install_error_handlers
from app.schemas import ErrorResponse

DESCRIPTION = """
반려견 추억 앱 **곁에** 백엔드. Unity 연동 안내는 `docs/api-for-unity.md`.

- **인증**: Supabase에서 받은 access token을 `Authorization: Bearer <token>`으로 보낸다.
  오른쪽 위 **Authorize**에 토큰만 넣으면 여기서 바로 호출해 볼 수 있다.
- **오류**: 모든 오류는 `{"error": {"code", "message"}}`. 화면 문구는 `code`로 고른다.
- **오래 걸리는 일**(4면·3D·리깅·파일 검사)은 `202 {"jobId"}`를 받고 `GET /jobs/{jobId}`를 3초마다 조회한다.
"""

app = FastAPI(
    title="곁에 API", version="0.1.0", description=DESCRIPTION,
    responses={"4XX": {"model": ErrorResponse, "description": "요청 오류 (공통 형식)"},
               "5XX": {"model": ErrorResponse, "description": "서버·외부 API 오류 (공통 형식)"}},
)
install_error_handlers(app)

app.include_router(health.router)
app.include_router(jobs.router)
app.include_router(character.router)
app.include_router(pets.router)
app.include_router(media.router)
app.include_router(items.router)
