"""3D 생성 — 이창민 (#49)

payload: {"characterModelId", "petId", "viewMediaIds": [side, front, back, other]}
         물건(#76)이면 characterModelId 대신 {"itemId", "itemKind"}
result:  {"characterModelId" 또는 "itemId", "rawGlbMediaId", "meshyTaskId"}
         app/pipeline.py가 character_models·items 행에 옮기고 다음 단계를 등록한다.

순서: 4면 이미지를 data URI로 Meshy multi-image-to-3d에 보냄 → 5초마다 상태 조회(ctx.progress)
      → GLB를 우리 저장소에 저장 → Meshy 작업 삭제 (ADR 2026-10-28-meshy-data-handling)
      docs/03-3d-pipeline.md 2장, docs/decisions/2026-10-28-meshy-input-mode.md 참고

재시도: Meshy 429·5xx·네트워크 오류·FAILED는 UPSTREAM_FAILED로 던져 큐가 다시 돌린다.
       캐릭터는 만든 Meshy 작업 ID를 character_models.meshy_task_id에 먼저 적어 두고,
       재시도 때 그 작업이 살아 있으면 새로 만들지 않고 이어서 조회한다 (크레딧 중복 방지).
크레딧 차단(#51): 새 작업을 만들기 전에 잔여 크레딧을 조회해 150 미만이면 LIMIT_EXCEEDED.
       이어서 조회하는 작업은 이미 청구됐으므로 막지 않는다.

MOCK_MESHY=true(기본값)면 Meshy를 호출하지 않고 샘플 GLB를 저장한다.
MOCK_MESHY_GLB에 GLB 경로를 주면 그 파일을, 없으면 삼각형 하나짜리 GLB를 쓴다.
"""
import base64
import json
import logging
import struct
import time
from pathlib import Path
from typing import Any

import httpx

from app import media
from app.config import get_settings
from app.db import get_pool
from app.errors import AppError, ErrorCode
from app.worker.handlers import JobContext

log = logging.getLogger("meshy_generate")

API = "https://api.meshy.ai/openapi/v1/multi-image-to-3d"
BALANCE_API = "https://api.meshy.ai/openapi/v1/balance"
GLB_MIME = "model/gltf-binary"
# 데모 직전에 크레딧이 바닥나지 않도록 약 5회분을 남긴다 (docs/07-cost-risk.md)
MIN_CREDITS = 150
POLL_SECONDS = 5
TIMEOUT_SECONDS = 15 * 60

# 면 수는 #50에서 황세인과 기준 폰 성능을 보고 확정한다
CHARACTER_POLYCOUNT = 30000
ITEM_POLYCOUNT = 5000

# 테스트에서 바꿔 끼운다
_transport: httpx.BaseTransport | None = None
_sleep = time.sleep


def handle(ctx: JobContext) -> dict[str, Any]:
    p = ctx.payload
    view_ids = p.get("viewMediaIds") or []
    if not 1 <= len(view_ids) <= 4:
        raise AppError(ErrorCode.VALIDATION_FAILED, "4면 이미지는 1~4장입니다")
    owner = {"itemId": p["itemId"]} if "itemId" in p else {
        "characterModelId": p.get("characterModelId")}

    views = [media.load(m, p["petId"]) for m in view_ids]
    ctx.progress(5)

    if get_settings().mock_meshy:
        glb, task_id = _mock_glb(), None
    else:
        glb, task_id = _generate(ctx, views, "itemId" in p)

    glb_id = media.save_generated(ctx.job["user_id"], p["petId"], glb, GLB_MIME,
                                  source_type="generated_model", parent_id=view_ids[0])
    if task_id:
        _delete_task(task_id)
    ctx.progress(100)
    return {**owner, "rawGlbMediaId": glb_id, "meshyTaskId": task_id}


def _generate(ctx: JobContext, views: list[tuple[bytes, str]], is_item: bool) -> tuple[bytes, str]:
    with _client() as client:
        task_id = _resume_task(client, ctx) or _create_task(client, ctx, views, is_item)
        task = _wait(client, ctx, task_id)
        glb = _call(client, "GET", task["model_urls"]["glb"], auth=False).content
    ctx.progress(95)
    return glb, task_id


def _client() -> httpx.Client:
    return httpx.Client(timeout=60, transport=_transport)


def _call(client: httpx.Client, method: str, url: str, auth: bool = True,
          **kwargs: Any) -> httpx.Response:
    """Meshy 오류를 큐 규칙에 맞는 AppError로 바꾼다."""
    headers = {"Authorization": f"Bearer {get_settings().meshy_api_key}"} if auth else {}
    try:
        res = client.request(method, url, headers=headers, **kwargs)
    except httpx.HTTPError as e:
        raise AppError(ErrorCode.UPSTREAM_FAILED, f"Meshy 연결 실패: {e}") from e
    if res.is_success:
        return res
    detail = f"Meshy {res.status_code}: {res.text[:300]}"
    if res.status_code == 429 or res.status_code >= 500:
        raise AppError(ErrorCode.UPSTREAM_FAILED, detail)
    if res.status_code == 402:  # 크레딧 부족
        raise AppError(ErrorCode.LIMIT_EXCEEDED, detail)
    if res.status_code in (400, 404, 422):
        raise AppError(ErrorCode.VALIDATION_FAILED, detail)
    raise AppError(ErrorCode.INTERNAL, detail)  # 401·403: 서버 키 설정 문제


def _check_credits(client: httpx.Client) -> None:
    balance = _call(client, "GET", BALANCE_API).json()["balance"]
    if balance < MIN_CREDITS:
        log.warning("Meshy 잔여 크레딧 %d < %d, 새 생성을 막습니다", balance, MIN_CREDITS)
        raise AppError(ErrorCode.LIMIT_EXCEEDED,
                       f"Meshy 잔여 크레딧 부족 ({balance} < {MIN_CREDITS})")


def _create_task(client: httpx.Client, ctx: JobContext, views: list[tuple[bytes, str]],
                 is_item: bool) -> str:
    _check_credits(client)
    body = {
        "image_urls": [f"data:{mime};base64,{base64.b64encode(data).decode()}"
                       for data, mime in views],
        "ai_model": "latest",
        "should_texture": True,
        "should_remesh": True,
        "topology": "triangle",
        "target_polycount": ITEM_POLYCOUNT if is_item else CHARACTER_POLYCOUNT,
        "target_formats": ["glb"],
    }
    task_id = _call(client, "POST", API, json=body).json()["result"]
    _remember_task(ctx, task_id)
    return task_id


def _wait(client: httpx.Client, ctx: JobContext, task_id: str) -> dict[str, Any]:
    deadline = time.monotonic() + TIMEOUT_SECONDS
    while True:
        task = _call(client, "GET", f"{API}/{task_id}").json()
        status = task["status"]
        if status == "SUCCEEDED":
            return task
        if status in ("FAILED", "CANCELED"):
            # 실패한 작업은 Meshy가 크레딧을 돌려준다. 큐가 새 작업으로 다시 시도한다
            _remember_task(ctx, None)
            message = (task.get("task_error") or {}).get("message", "")
            raise AppError(ErrorCode.UPSTREAM_FAILED, f"Meshy {status}: {message}")
        if time.monotonic() > deadline:
            raise AppError(ErrorCode.UPSTREAM_FAILED, f"Meshy 시간 초과 ({TIMEOUT_SECONDS}초)")
        ctx.progress(5 + task.get("progress", 0) * 85 // 100)
        _sleep(POLL_SECONDS)


def _delete_task(task_id: str) -> None:
    """ADR: GLB를 복사한 즉시 지운다. 실패해도 Meshy가 3일 뒤 지우므로 작업은 성공으로 둔다."""
    try:
        with _client() as client:
            _call(client, "DELETE", f"{API}/{task_id}")
    except AppError as e:
        log.warning("Meshy 작업 삭제 실패 %s: %s", task_id, e.message)


# ---- 재시도 때 크레딧 중복 방지 (캐릭터만. 물건은 테이블에 작업 ID 칸이 없다) ----

def _resume_task(client: httpx.Client, ctx: JobContext) -> str | None:
    task_id = _saved_task_id(ctx)
    if not task_id:
        return None
    try:
        status = _call(client, "GET", f"{API}/{task_id}").json()["status"]
    except AppError as e:
        if e.code != ErrorCode.VALIDATION_FAILED:  # 404(이미 지워짐)면 새로 만든다
            raise
        return None
    return task_id if status in ("PENDING", "IN_PROGRESS", "SUCCEEDED") else None


def _saved_task_id(ctx: JobContext) -> str | None:
    cm_id = ctx.payload.get("characterModelId")
    if not cm_id or not ctx.job.get("id"):
        return None
    with get_pool().connection() as conn:
        row = conn.execute(
            "select meshy_task_id from character_models where id = %s and job_id = %s",
            (cm_id, ctx.job["id"]),
        ).fetchone()
    return row["meshy_task_id"] if row else None


def _remember_task(ctx: JobContext, task_id: str | None) -> None:
    """그 행의 현재 작업일 때만 적는다 (pipeline._update와 같은 규칙)."""
    cm_id = ctx.payload.get("characterModelId")
    if not cm_id or not ctx.job.get("id"):
        return
    with get_pool().connection() as conn:
        conn.execute("update character_models set meshy_task_id = %s, updated_at = now() "
                     "where id = %s and job_id = %s", (task_id, cm_id, ctx.job["id"]))


# ---- 목업 ----

def _mock_glb() -> bytes:
    path = get_settings().mock_meshy_glb
    return Path(path).read_bytes() if path else _triangle_glb()


def _triangle_glb() -> bytes:
    """삼각형 하나짜리 유효한 GLB. 파이프라인 연결 확인용이다."""
    positions = struct.pack("<9f", 0, 0, 0, 1, 0, 0, 0, 1, 0)
    gltf = {
        "asset": {"version": "2.0"},
        "scenes": [{"nodes": [0]}], "scene": 0,
        "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0}}]}],
        "accessors": [{"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3",
                       "min": [0, 0, 0], "max": [1, 1, 0]}],
        "bufferViews": [{"buffer": 0, "byteLength": len(positions)}],
        "buffers": [{"byteLength": len(positions)}],
    }
    text = json.dumps(gltf).encode()
    text += b" " * (-len(text) % 4)
    chunks = (struct.pack("<II", len(text), 0x4E4F534A) + text
              + struct.pack("<II", len(positions), 0x004E4942) + positions)
    return struct.pack("<4sII", b"glTF", 2, 12 + len(chunks)) + chunks
