"""자동 리깅 — 황세인 (#3)

payload: {"characterModelId", "preparedGlbPath", "joints": Joints}
result:  {"characterModelId", "riggedGlbPath", "skeletonVersion"}

예시 흐름:
    with tempfile.TemporaryDirectory() as d:
        # 버킷에서 GLB 내려받기, joints.json 쓰기
        run_blender("rig.py", f"{d}/in.glb", f"{d}/joints.json", f"{d}/out.glb")
        # out.glb를 버킷에 올리기
docs/03-3d-pipeline.md 5장 참고
"""
from typing import Any

from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    raise NotImplementedError("rig: 황세인 구현 예정 (#3)")
