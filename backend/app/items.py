"""나의 방 물건: 장난감·간식·애착자리 (supabase/migrations/0005_items.sql, #76).

    POST /pets/:id/items        사진 → 누끼 작업(item_cutout)
    GET  /pets/:id/items        목록 + 상태
    POST /pets/:id/toys/gacha   2D 장난감 4개 이상일 때 무작위 1개 → 4면(item_views) → 3D

작업 결과를 행으로 옮기고 다음 단계를 등록하는 일은 app/pipeline.py가 한다.
포인트는 앱 화면 연출만 한다 (서버에는 잔액·차감이 없다).
"""
import random
from uuid import UUID

from app import characters, media, queue
from app.db import get_pool
from app.errors import AppError, ErrorCode
from app.schemas import ItemKind, ItemStatus, JobType

GACHA_MIN_CANDIDATES = 4


def _set_job(item_id: UUID, job_id: UUID) -> None:
    with get_pool().connection() as conn:
        conn.execute("update items set job_id = %s, updated_at = now() where id = %s",
                     (job_id, item_id))


def create(user_id: UUID, pet_id: UUID, kind: ItemKind, source_id: UUID,
           idempotency_key: str | None) -> tuple[UUID, UUID]:
    """(itemId, 누끼 jobId). 같은 Idempotency-Key면 같은 물건."""
    characters._check_sources(pet_id, [source_id])
    with get_pool().connection() as conn:
        item = conn.execute(
            """
            insert into items (user_id, pet_id, kind, source_media_id, idempotency_key)
            values (%s, %s, %s, %s, %s)
            on conflict (user_id, idempotency_key) do nothing returning *
            """,
            (user_id, pet_id, kind, source_id, idempotency_key),
        ).fetchone()
        if item is None:
            existing = conn.execute(
                "select * from items where user_id = %s and idempotency_key = %s",
                (user_id, idempotency_key),
            ).fetchone()
            return existing["id"], characters._existing_job(existing)
    job_id = queue.enqueue(
        JobType.ITEM_CUTOUT, user_id,
        {"itemId": str(item["id"]), "petId": str(pet_id), "sourceMediaId": str(source_id)},
        pet_id=pet_id, idempotency_key=f"item_cutout:{item['id']}",
    )
    _set_job(item["id"], job_id)
    return item["id"], job_id


def _status(row: dict) -> ItemStatus:
    if row["glb_media_id"]:
        return ItemStatus.READY
    if row["model_requested_at"]:
        return ItemStatus.GENERATING
    return ItemStatus.CUTOUT if row["cutout_media_id"] else ItemStatus.CUTTING


def _url(media_id: UUID | None) -> str | None:
    return media.download_url(media.get(media_id)) if media_id else None


def list_items(pet_id: UUID, kind: ItemKind | None = None) -> list[dict]:
    with get_pool().connection() as conn:
        rows = conn.execute(
            """
            select * from items where pet_id = %s and (%s::text is null or kind = %s)
            order by created_at
            """,
            (pet_id, kind, kind),
        ).fetchall()
    return [{**r, "status": _status(r), "cutout_url": _url(r["cutout_media_id"]),
             "glb_url": _url(r["glb_media_id"])} for r in rows]


def request_model(item: dict, idempotency_key: str | None = None) -> UUID:
    """4면·3D 생성 시작. 가챠(장난감)와 누끼 후처리(간식·애착자리)가 부른다."""
    job_id = queue.enqueue(
        JobType.ITEM_VIEWS, item["user_id"],
        {"itemId": str(item["id"]), "petId": str(item["pet_id"]), "kind": item["kind"],
         "cutoutMediaId": str(item["cutout_media_id"])},
        pet_id=item["pet_id"], idempotency_key=idempotency_key or f"item_views:{item['id']}",
    )
    with get_pool().connection() as conn:
        conn.execute(
            """
            update items set job_id = %s, model_requested_at = coalesce(model_requested_at, now()),
                   updated_at = now()
            where id = %s
            """,
            (job_id, item["id"]),
        )
    return job_id


def gacha(user_id: UUID, pet_id: UUID, idempotency_key: str | None) -> tuple[UUID, UUID]:
    """(뽑힌 itemId, 4면 jobId). 같은 Idempotency-Key로 다시 누르면 같은 결과."""
    key = f"gacha:{idempotency_key}" if idempotency_key else None
    with get_pool().connection() as conn, conn.transaction():
        # 같은 반려견으로 동시에 돌려도 같은 장난감이 두 번 뽑히지 않게 잠근다.
        # 작업 등록(request_model)까지 잠근 채로 끝내야 다음 요청이 뽑힌 결과를 본다.
        conn.execute("select 1 from pets where id = %s for update", (pet_id,))
        if key:
            done = conn.execute(
                "select id, payload from jobs where user_id = %s and idempotency_key = %s",
                (user_id, key),
            ).fetchone()
            if done:
                return UUID(done["payload"]["itemId"]), done["id"]
        candidates = conn.execute(
            """
            select * from items
            where pet_id = %s and kind = 'toy' and cutout_media_id is not null
              and model_requested_at is null
            """,
            (pet_id,),
        ).fetchall()
        if len(candidates) < GACHA_MIN_CANDIDATES:
            raise AppError(ErrorCode.CONFLICT,
                           f"아직 3D가 아닌 장난감이 {GACHA_MIN_CANDIDATES}개 이상 있어야 합니다 "
                           f"(지금 {len(candidates)}개)")
        item = random.choice(candidates)
        return item["id"], request_model(item, key)
