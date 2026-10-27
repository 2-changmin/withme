"""메시 정리 + 관절 자동 추정 — 황세인 (#4)

payload: {"characterModelId", "rawGlbPath"}
result:  {"characterModelId", "preparedGlbPath", "jointsAuto": Joints}

blender/prepare.py를 run_blender로 실행한다. docs/03-3d-pipeline.md 3장 참고
"""
from typing import Any

from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    raise NotImplementedError("mesh_prepare: 황세인 구현 예정 (#4)")
