"""작업이 성공한 뒤의 후처리 (장세찬 #23). 작업자가 queue.complete 직후에 부른다.

작업 결과를 reference_views·character_models 행으로 옮기고 다음 단계를 등록한다.
    reference_views  → 행에 4면 저장 (승인은 보호자가 한다)
    meshy_generate   → 원본 GLB 저장 → mesh_prepare 등록
    mesh_prepare     → 정리된 GLB·자동 관절 저장 (관절 보정은 보호자가 한다)
    rig              → 리깅 GLB 저장
    letter_reply     → 답장(또는 위기 안내 상태)을 letters 행에 저장 (#74)
처리 함수 담당자는 result에 아래 키를 담아 주면 된다 (파일은 모두 media ID).
"""
import logging
from typing import Any
from uuid import UUID

from psycopg.types.json import Jsonb

from app import queue
from app.db import get_pool
from app.schemas import JobType

log = logging.getLogger("pipeline")


def on_succeeded(job: dict[str, Any], result: dict[str, Any]) -> None:
    handler = _AFTER.get(job["type"])
    if handler:
        handler(job, job["payload"], result)


def _uuid(value: str | None) -> UUID | None:
    return UUID(value) if value else None


def _jsonb(value: Any) -> Jsonb | None:
    return Jsonb(value) if value is not None else None


def _update(table: str, row_id: str, job_id: UUID, sets: str, params: tuple) -> dict | None:
    """그 행의 현재 작업일 때만 바꾼다 (재생성·재리깅으로 바뀐 뒤 늦게 끝난 작업은 무시)."""
    with get_pool().connection() as conn:
        return conn.execute(
            f"update {table} set {sets}, updated_at = now() "
            "where id = %s and job_id = %s returning *",
            (*params, row_id, job_id),
        ).fetchone()


def _reference_views(job: dict, p: dict, r: dict) -> None:
    _update("reference_views", p["referenceViewId"], job["id"],
            "view_media_ids = %s", ([UUID(m) for m in r["viewMediaIds"]],))


def _meshy_generate(job: dict, p: dict, r: dict) -> None:
    raw = r.get("rawGlbMediaId")
    cm = _update("character_models", p["characterModelId"], job["id"],
                 "raw_glb_media_id = %s, meshy_task_id = %s", (_uuid(raw), r.get("meshyTaskId")))
    if cm is None:
        return
    if raw is None:  # 목 결과(rawGlbPath)에는 파일이 없어서 다음 단계로 넘길 수 없다
        log.warning("meshy_generate 결과에 rawGlbMediaId가 없어 mesh_prepare를 등록하지 않습니다 %s",
                    job["id"])
        return
    job_id = queue.enqueue(
        JobType.MESH_PREPARE, job["user_id"],
        {"characterModelId": str(cm["id"]), "petId": str(cm["pet_id"]), "rawGlbMediaId": raw},
        pet_id=cm["pet_id"], idempotency_key=f"mesh_prepare:{cm['id']}",
    )
    with get_pool().connection() as conn:
        conn.execute("update character_models set job_id = %s, updated_at = now() where id = %s",
                     (job_id, cm["id"]))


def _mesh_prepare(job: dict, p: dict, r: dict) -> None:
    _update("character_models", p["characterModelId"], job["id"],
            "prepared_glb_media_id = %s, joints_auto = %s",
            (_uuid(r.get("preparedGlbMediaId")), _jsonb(r.get("jointsAuto"))))


def _rig(job: dict, p: dict, r: dict) -> None:
    _update("character_models", p["characterModelId"], job["id"],
            "rigged_glb_media_id = %s, skeleton_version = coalesce(%s, skeleton_version)",
            (_uuid(r.get("riggedGlbMediaId")), r.get("skeletonVersion")))


def _letter_reply(job: dict, p: dict, r: dict) -> None:
    _update("letters", p["letterId"], job["id"],
            "reply = %s, status = %s, replied_at = now()",
            (r.get("reply"), "crisis" if r.get("crisis") else "replied"))


_AFTER = {
    JobType.REFERENCE_VIEWS: _reference_views,
    JobType.MESHY_GENERATE: _meshy_generate,
    JobType.MESH_PREPARE: _mesh_prepare,
    JobType.RIG: _rig,
    JobType.LETTER_REPLY: _letter_reply,
}
