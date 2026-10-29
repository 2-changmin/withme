"""물건 4면 이미지 — 윤승희 (#76)

payload: {"itemId", "petId", "kind", "cutoutMediaId"}
result:  {"itemId", "viewMediaIds": [side, front, back, other]}

누끼를 흰 배경에 얹어 Gemini로 4면을 만든다. 끝나면 app/pipeline.py가 meshy_generate를 등록한다.
MOCK_GEMINI=true(기본값)면 Gemini를 부르지 않고 누끼를 4면 결과로 저장한다.
"""
from typing import Any

from app import media
from app.errors import AppError, ErrorCode
from app.worker import gemini_views
from app.worker.gemini_views import ITEM_NAMES, VIEWS
from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    p = ctx.payload
    if p.get("kind") not in ITEM_NAMES:
        raise AppError(ErrorCode.VALIDATION_FAILED, f"모르는 물건 종류: {p.get('kind')}")
    cutout = media.load(p["cutoutMediaId"], p["petId"])
    ctx.progress(5)
    views = gemini_views.generate_item_views(cutout, p["kind"],
                                             lambda n: ctx.progress(5 + n * 90 // 100))
    view_ids = [media.save_generated(ctx.job["user_id"], p["petId"], *views[v],
                                     source_type="generated_visual", parent_id=p["cutoutMediaId"])
                for v in VIEWS]
    return {"itemId": p["itemId"], "viewMediaIds": view_ids}
