"""4면 이미지 생성 — 윤승희 (#42, 프롬프트·순서는 #39)

payload: {"petId", "referenceViewId", "sourceMediaIds": [1~4개], "attempt": 1~3}
result:  {"referenceViewId", "viewMediaIds": [side, front, back, other],
          "problems": {면: [문제, ...]}}   # 검사에 걸린 채 남은 면만. 4면 확인 화면에서 보여 준다

순서: 원본 → 반려견 영역 자르기 → 서 있는 옆모습 기준 1장 → 기준 + 원본으로 나머지 3면
      → 면별 검사(실패 시 1회 재생성) → 저장. docs/03-3d-pipeline.md 1장 참고

MOCK_GEMINI=true(기본값)면 Gemini를 부르지 않고 원본 사진을 4면 결과로 저장한다.
"""
from typing import Any

from app import media, pets
from app.errors import AppError, ErrorCode
from app.schemas import ConsentItem
from app.worker import gemini_views
from app.worker.gemini_views import VIEWS
from app.worker.handlers import JobContext

MAX_ATTEMPTS = 3


def handle(ctx: JobContext) -> dict[str, Any]:
    p = ctx.payload
    pet_id = p.get("petId")
    if not pet_id:
        raise AppError(ErrorCode.VALIDATION_FAILED, "petId가 없습니다")
    # API(#23)가 막더라도 외부 전송 직전에 한 번 더 확인한다 (docs/decisions/2026-10-28-gemini-data-handling.md)
    if not any(c["item"] == ConsentItem.GEMINI_TRANSFER for c in pets.latest_consents(pet_id)):
        raise AppError(ErrorCode.FORBIDDEN, "Gemini 전달 동의가 없습니다")
    source_ids = p.get("sourceMediaIds") or []
    if not 1 <= len(source_ids) <= 4:
        raise AppError(ErrorCode.VALIDATION_FAILED, "원본 사진은 1~4장입니다")
    if p.get("attempt", 1) > MAX_ATTEMPTS:
        raise AppError(ErrorCode.LIMIT_EXCEEDED, f"4면 재생성은 최대 {MAX_ATTEMPTS}회입니다")

    sources = [media.load(m, pet_id) for m in source_ids]
    ctx.progress(5)
    views, problems = gemini_views.generate_views(
        sources, lambda n: ctx.progress(5 + n * 90 // 100))
    # 4면 이미지는 원본 첫 장에서 만들어진 것으로 기록한다
    view_ids = [media.save_generated(ctx.job["user_id"], pet_id, *views[v],
                                     source_type="generated_visual", parent_id=source_ids[0])
                for v in VIEWS]

    return {"referenceViewId": p.get("referenceViewId"), "viewMediaIds": view_ids,
            "problems": problems}
