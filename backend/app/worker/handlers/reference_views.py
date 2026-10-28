"""4면 이미지 생성 — 윤승희 (#42, 프롬프트·순서는 #39)

payload: {"petId", "referenceViewId", "sourceMediaIds": [1~4개], "attempt": 1~3}
result:  {"referenceViewId", "viewMediaIds": [side, front, back, other],
          "problems": {면: [문제, ...]}}   # 검사에 걸린 채 남은 면만. 4면 확인 화면에서 보여 준다

순서: 원본 → 반려견 영역 자르기 → 서 있는 옆모습 기준 1장 → 기준 + 원본으로 나머지 3면
      → 면별 검사(실패 시 1회 재생성) → 저장. docs/03-3d-pipeline.md 1장 참고

MOCK_GEMINI=true(기본값)면 Gemini를 부르지 않고 원본 사진을 4면 결과로 저장한다.
"""
import mimetypes
import uuid
from pathlib import Path
from typing import Any

from app.config import get_settings
from app.errors import AppError, ErrorCode
from app.worker import gemini_views
from app.worker.gemini_views import VIEWS, Image
from app.worker.handlers import JobContext

MAX_ATTEMPTS = 3


# ponytail: 박재현 #30(media_assets·비공개 버킷)이 들어오기 전까지 로컬 폴더에 저장한다.
# #30이 들어오면 아래 두 함수만 그 저장소 헬퍼로 바꾼다. 나머지 흐름은 그대로.
def _media_dir() -> Path:
    return Path(get_settings().local_media_dir)


def load_media(media_id: str) -> Image:
    found = list(_media_dir().glob(f"{media_id}.*"))
    if not found:
        raise AppError(ErrorCode.VALIDATION_FAILED, f"원본 사진이 없음: {media_id}")
    return found[0].read_bytes(), mimetypes.guess_type(found[0])[0] or "image/jpeg"


def save_media(data: bytes, mime: str) -> str:
    media_id = str(uuid.uuid4())
    _media_dir().mkdir(parents=True, exist_ok=True)
    (_media_dir() / f"{media_id}{mimetypes.guess_extension(mime) or '.png'}").write_bytes(data)
    return media_id


def handle(ctx: JobContext) -> dict[str, Any]:
    p = ctx.payload
    source_ids = p.get("sourceMediaIds") or []
    if not 1 <= len(source_ids) <= 4:
        raise AppError(ErrorCode.VALIDATION_FAILED, "원본 사진은 1~4장입니다")
    if p.get("attempt", 1) > MAX_ATTEMPTS:
        raise AppError(ErrorCode.LIMIT_EXCEEDED, f"4면 재생성은 최대 {MAX_ATTEMPTS}회입니다")

    sources = [load_media(m) for m in source_ids]
    ctx.progress(5)
    views, problems = gemini_views.generate_views(
        sources, lambda n: ctx.progress(5 + n * 90 // 100))
    view_ids = [save_media(*views[v]) for v in VIEWS]

    return {"referenceViewId": p.get("referenceViewId"), "viewMediaIds": view_ids,
            "problems": problems}
