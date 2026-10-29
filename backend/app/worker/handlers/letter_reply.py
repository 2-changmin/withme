"""무지개다리 편지 답장 — 박재현 (#74)

payload: {"letterId"}
result:  {"letterId", "crisis", "reply"}   # pipeline이 letters 행으로 옮긴다

편지 본문은 payload에 싣지 않고 letters 테이블에서 읽는다.
MOCK_LETTER=true(기본값)면 Gemini를 부르지 않고 정해진 답장을 쓴다.
"""
from typing import Any

from app import letters
from app.errors import AppError, ErrorCode
from app.worker import letter_writer
from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    letter_id = ctx.payload.get("letterId")
    if not letter_id:
        raise AppError(ErrorCode.VALIDATION_FAILED, "letterId가 없습니다")
    row = letters.load_for_reply(letter_id)
    ctx.progress(10)
    out = letter_writer.write_reply(row["pet_name"], row["body"])
    return {"letterId": str(row["id"]), **out}
