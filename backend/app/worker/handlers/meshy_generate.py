"""3D 생성 — 이창민 (#49)

payload: {"characterModelId", "viewUrls": [서명 URL 1~4개]}
result:  {"characterModelId", "rawGlbPath"}

순서: Meshy 작업 생성 → 5초마다 상태 조회(ctx.progress 반영) → GLB를 우리 버킷으로 복사
      → Meshy 작업 삭제. docs/03-3d-pipeline.md 2장, docs/tools/meshy_test.py 참고

MOCK_MESHY=true(기본값)면 Meshy를 호출하지 않고 가짜 결과를 돌려준다.
"""
from typing import Any

from app.config import get_settings
from app.worker.handlers import JobContext

MOCK_RAW_GLB_PATH = "mock/raw.glb"


def handle(ctx: JobContext) -> dict[str, Any]:
    if get_settings().mock_meshy:
        return _mock(ctx)
    raise NotImplementedError("meshy_generate: 이창민 구현 예정 (#49)")


def _mock(ctx: JobContext) -> dict[str, Any]:
    for percent in (30, 60, 100):
        ctx.progress(percent)
    return {"characterModelId": ctx.payload["characterModelId"], "rawGlbPath": MOCK_RAW_GLB_PATH}
