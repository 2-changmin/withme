"""물건 누끼 — 윤승희 (#76)

payload: {"itemId", "petId", "sourceMediaId"}
result:  {"itemId", "cutoutMediaId"}   # 투명 배경 PNG

기기 안에서 배경을 지운다 (외부 전송 없음, 비용 없음). 다음 단계는 app/pipeline.py가 정한다.
"""
from typing import Any

from app import media
from app.worker import photo_prep
from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    p = ctx.payload
    data, _ = media.load(p["sourceMediaId"], p["petId"])
    ctx.progress(10)
    png, mime = photo_prep.cutout(data)
    cutout_id = media.save_generated(ctx.job["user_id"], p["petId"], png, mime,
                                     source_type="generated_visual", parent_id=p["sourceMediaId"])
    return {"itemId": p["itemId"], "cutoutMediaId": cutout_id}
