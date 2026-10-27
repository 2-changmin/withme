"""3D 생성 — 이창민 (#49)

payload: {"characterModelId", "viewUrls": [서명 URL 1~4개]}
result:  {"characterModelId", "rawGlbPath"}

순서: Meshy 작업 생성 → 5초마다 상태 조회(ctx.progress 반영) → GLB를 우리 버킷으로 복사
      → Meshy 작업 삭제. docs/03-3d-pipeline.md 2장, docs/tools/meshy_test.py 참고
"""
from typing import Any

from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    raise NotImplementedError("meshy_generate: 이창민 구현 예정 (#49)")
