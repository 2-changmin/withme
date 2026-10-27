"""4면 이미지 생성 — 윤승희 (#42, 프롬프트·순서는 #39)

payload: {"petId", "sourceMediaIds": [...], "attempt": 1~3}
result:  {"referenceViewId", "viewMediaIds": [side, front, back, other]}

순서: 원본 여러 장 → 서 있는 옆모습 기준 1장 → 기준 + 원본으로 나머지 3면
docs/03-3d-pipeline.md 1장 참고
"""
from typing import Any

from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    raise NotImplementedError("reference_views: 윤승희 구현 예정 (#42)")
