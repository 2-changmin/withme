"""4면 이미지 → 3D 생성 → 관절 → 리깅 요청 처리 (supabase/migrations/0004, F04~F07).

API는 여기 함수로 행을 만들고 작업을 등록한다. 작업 결과를 행으로 옮기고 다음 단계를
등록하는 일은 작업자 쪽 app/pipeline.py가 한다.

외부 전송 전 동의 확인 (docs/decisions/2026-10-28-*-data-handling.md)
- 4면 생성·재생성: gemini_transfer
- 4면 승인(3D 생성): meshy_transfer
작업자도 전송 직전에 한 번 더 확인한다.

중복 청구 방지
- 4면 생성: Idempotency-Key 헤더가 같으면 같은 작업
- 재생성: 같은 시도에서 재생성은 한 번만 (reference_views.retry_of unique)
- 승인: 4면 1세트에 3D 모델 하나 (character_models.reference_view_id unique)
"""
from typing import Any
from uuid import UUID

from psycopg.types.json import Jsonb

from app import media, pets, queue
from app.db import get_pool
from app.errors import AppError, ErrorCode
from app.schemas import ConsentItem, JobType, Joints

MAX_REFERENCE_ATTEMPTS = 3

_CONSENT_MESSAGES = {
    ConsentItem.GEMINI_TRANSFER: "사진을 Gemini에 보내는 데 동의하지 않았습니다",
    ConsentItem.MESHY_TRANSFER: "이미지를 Meshy에 보내는 데 동의하지 않았습니다",
}


def _parse_id(value: str, what: str) -> UUID:
    try:
        return UUID(value)
    except ValueError as e:
        raise AppError(ErrorCode.NOT_FOUND, f"{what}이 없습니다") from e


def require_consent(pet_id: UUID, item: ConsentItem) -> None:
    if not any(c["item"] == item for c in pets.latest_consents(pet_id)):
        raise AppError(ErrorCode.FORBIDDEN, _CONSENT_MESSAGES[item])


def _check_sources(pet_id: UUID, source_ids: list[UUID]) -> None:
    """원본은 이 반려견이 올린 사진이어야 한다. 남의 파일이든 없는 파일이든 같은 오류."""
    for media_id in source_ids:
        try:
            row = media.get(media_id)
        except AppError:
            row = None
        if (row is None or row["pet_id"] != pet_id or row["source_type"] != "upload"
                or not row["mime_type"].startswith("image/")):
            raise AppError(ErrorCode.VALIDATION_FAILED, f"사용할 수 없는 사진: {media_id}")
        if row["status"] not in ("uploaded", "ready"):
            raise AppError(ErrorCode.VALIDATION_FAILED, f"아직 쓸 수 없는 사진: {media_id}")


def _owned(table: str, row_id: str, user_id: UUID, what: str) -> dict:
    with get_pool().connection() as conn:
        row = conn.execute(
            f"select * from {table} where id = %s and user_id = %s",
            (_parse_id(row_id, what), user_id),
        ).fetchone()
    if row is None:
        raise AppError(ErrorCode.NOT_FOUND, f"{what}이 없습니다")
    return row


def _set_job(table: str, row_id: UUID, job_id: UUID) -> None:
    with get_pool().connection() as conn:
        conn.execute(f"update {table} set job_id = %s, updated_at = now() where id = %s",
                     (job_id, row_id))


def _existing_job(row: dict) -> UUID:
    if row["job_id"] is None:  # 같은 요청이 동시에 들어와 아직 등록 중
        raise AppError(ErrorCode.CONFLICT, "같은 요청을 처리하고 있습니다")
    return row["job_id"]


def _enqueue_reference_views(rv: dict) -> UUID:
    job_id = queue.enqueue(
        JobType.REFERENCE_VIEWS, rv["user_id"],
        {"petId": str(rv["pet_id"]), "referenceViewId": str(rv["id"]),
         "sourceMediaIds": [str(m) for m in rv["source_media_ids"]], "attempt": rv["attempt"]},
        pet_id=rv["pet_id"], idempotency_key=f"reference_views:{rv['id']}",
    )
    _set_job("reference_views", rv["id"], job_id)
    return job_id


# ---- 4면 이미지 (F04) ----

def create_reference_views(user_id: UUID, pet_id: UUID, source_ids: list[UUID],
                           idempotency_key: str | None) -> UUID:
    require_consent(pet_id, ConsentItem.GEMINI_TRANSFER)
    source_ids = list(dict.fromkeys(source_ids))
    _check_sources(pet_id, source_ids)
    with get_pool().connection() as conn:
        rv = conn.execute(
            """
            insert into reference_views (user_id, pet_id, source_media_ids, idempotency_key)
            values (%s, %s, %s, %s)
            on conflict (user_id, idempotency_key) do nothing returning *
            """,
            (user_id, pet_id, source_ids, idempotency_key),
        ).fetchone()
        if rv is None:
            existing = conn.execute(
                "select * from reference_views where user_id = %s and idempotency_key = %s",
                (user_id, idempotency_key),
            ).fetchone()
            return _existing_job(existing)
    return _enqueue_reference_views(rv)


def retry_reference_views(user_id: UUID, reference_view_id: str) -> UUID:
    prev = _owned("reference_views", reference_view_id, user_id, "4면 이미지")
    require_consent(prev["pet_id"], ConsentItem.GEMINI_TRANSFER)
    if prev["approved_at"] is not None:
        raise AppError(ErrorCode.CONFLICT, "이미 확정한 4면 이미지입니다")
    if prev["attempt"] >= MAX_REFERENCE_ATTEMPTS:
        raise AppError(ErrorCode.LIMIT_EXCEEDED,
                       f"4면 재생성은 최대 {MAX_REFERENCE_ATTEMPTS}회입니다")
    with get_pool().connection() as conn:
        rv = conn.execute(
            """
            insert into reference_views (user_id, pet_id, source_media_ids, attempt, retry_of)
            values (%s, %s, %s, %s, %s)
            on conflict (retry_of) do nothing returning *
            """,
            (user_id, prev["pet_id"], prev["source_media_ids"], prev["attempt"] + 1, prev["id"]),
        ).fetchone()
        if rv is None:
            existing = conn.execute("select * from reference_views where retry_of = %s",
                                    (prev["id"],)).fetchone()
            return _existing_job(existing)
    return _enqueue_reference_views(rv)


def _view_media_ids(rv: dict) -> list[UUID]:
    """4면 결과. 작업자 후처리가 아직 옮기지 못했으면 작업 결과에서 직접 읽는다."""
    if rv["view_media_ids"]:
        return rv["view_media_ids"]
    job = queue.get_job(rv["job_id"], rv["user_id"]) if rv["job_id"] else None
    if job is None or job["status"] != "succeeded" or not (job["result"] or {}).get("viewMediaIds"):
        raise AppError(ErrorCode.CONFLICT, "4면 이미지가 아직 준비되지 않았습니다")
    return [UUID(m) for m in job["result"]["viewMediaIds"]]


# ---- 3D 생성 (F05) ----

def approve_reference_views(user_id: UUID, reference_view_id: str) -> UUID:
    rv = _owned("reference_views", reference_view_id, user_id, "4면 이미지")
    require_consent(rv["pet_id"], ConsentItem.MESHY_TRANSFER)
    view_ids = _view_media_ids(rv)
    with get_pool().connection() as conn:
        conn.execute(
            """
            update reference_views set view_media_ids = %s,
                   approved_at = coalesce(approved_at, now()), updated_at = now()
            where id = %s
            """,
            (view_ids, rv["id"]),
        )
        cm = conn.execute(
            """
            insert into character_models (user_id, pet_id, reference_view_id)
            values (%s, %s, %s)
            on conflict (reference_view_id) do nothing returning *
            """,
            (user_id, rv["pet_id"], rv["id"]),
        ).fetchone()
        if cm is None:
            existing = conn.execute("select * from character_models where reference_view_id = %s",
                                    (rv["id"],)).fetchone()
            return _existing_job(existing)
    job_id = queue.enqueue(
        JobType.MESHY_GENERATE, user_id,
        {"characterModelId": str(cm["id"]), "petId": str(rv["pet_id"]),
         "viewMediaIds": [str(m) for m in view_ids]},
        pet_id=rv["pet_id"], idempotency_key=f"meshy_generate:{cm['id']}",
    )
    _set_job("character_models", cm["id"], job_id)
    return job_id


# ---- 관절·리깅 (F06·F07) ----

def get_joints(user_id: UUID, character_model_id: str) -> Joints:
    cm = _owned("character_models", character_model_id, user_id, "3D 모델")
    if cm["joints_auto"] is None:
        raise AppError(ErrorCode.CONFLICT, "관절 추정이 아직 끝나지 않았습니다")
    return Joints.model_validate(cm["joints_auto"])


def rig(user_id: UUID, character_model_id: str, joints: Joints,
        idempotency_key: str | None) -> UUID:
    """관절을 다시 지정하면 다시 리깅할 수 있다. 같은 Idempotency-Key면 같은 작업."""
    cm = _owned("character_models", character_model_id, user_id, "3D 모델")
    if cm["prepared_glb_media_id"] is None:
        raise AppError(ErrorCode.CONFLICT, "메시 정리가 아직 끝나지 않았습니다")
    joints_json: dict[str, Any] = joints.model_dump(mode="json", by_alias=True)
    with get_pool().connection() as conn:
        conn.execute(
            "update character_models set joints_final = %s, updated_at = now() where id = %s",
            (Jsonb(joints_json), cm["id"]),
        )
    job_id = queue.enqueue(
        JobType.RIG, user_id,
        {"characterModelId": str(cm["id"]), "petId": str(cm["pet_id"]),
         "preparedGlbMediaId": str(cm["prepared_glb_media_id"]), "joints": joints_json},
        pet_id=cm["pet_id"],
        idempotency_key=f"rig:{cm['id']}:{idempotency_key}" if idempotency_key else None,
    )
    _set_job("character_models", cm["id"], job_id)
    return job_id
