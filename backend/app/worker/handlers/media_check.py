"""업로드 파일 검사 — 박재현 (#32)

payload: {"mediaId"}
result:  {"mediaId", "status"}

지금은 업로드가 끝난(uploaded) 파일을 ready로 바꾸기만 한다. 크기는 finalize에서 이미 확인했다.
남은 일 (#32): 파일 시그니처·디코딩 확인, 썸네일, 회전 보정, 위치 메타데이터 제거
"""
from typing import Any

from app import media
from app.errors import AppError, ErrorCode
from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    media_id = ctx.payload.get("mediaId")
    if not media_id:
        raise AppError(ErrorCode.VALIDATION_FAILED, "mediaId가 없습니다")
    row = media.mark_ready(media_id)
    return {"mediaId": str(row["id"]), "status": row["status"]}
