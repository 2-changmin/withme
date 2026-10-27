"""업로드 파일 검사 — 박재현 (#32)

payload: {"mediaId"}
result:  {"mediaId", "thumbnailPath"}

파일 시그니처·디코딩 확인, 썸네일, 회전 보정, 위치 메타데이터 제거
"""
from typing import Any

from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    raise NotImplementedError("media_check: 박재현 구현 예정 (#32)")
