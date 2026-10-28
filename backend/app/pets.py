"""반려견 프로필·동의 저장소 (supabase/migrations/0002_users_pets_consents.sql).

소유권은 라우트의 OwnedPet(app/ownership.py)이 먼저 확인한다. 여기 함수는 확인된 pet_id만 받는다.
"""
from uuid import UUID

from psycopg import errors

from app.db import get_pool
from app.errors import AppError, ErrorCode
from app.schemas import ConsentItem


def create(user_id: UUID, name: str) -> dict:
    """계정당 1마리. 이미 있으면 CONFLICT."""
    with get_pool().connection() as conn:
        conn.execute("insert into users (id) values (%s) on conflict do nothing", (user_id,))
        try:
            return conn.execute(
                "insert into pets (user_id, name) values (%s, %s) returning *", (user_id, name)
            ).fetchone()
        except errors.UniqueViolation as e:
            raise AppError(ErrorCode.CONFLICT, "이미 등록한 반려견이 있습니다") from e


def list_for_user(user_id: UUID) -> list[dict]:
    with get_pool().connection() as conn:
        return conn.execute(
            "select * from pets where user_id = %s order by created_at", (user_id,)
        ).fetchall()


def get(pet_id: UUID) -> dict:
    with get_pool().connection() as conn:
        return conn.execute("select * from pets where id = %s", (pet_id,)).fetchone()


def update(pet_id: UUID, name: str | None) -> dict:
    with get_pool().connection() as conn:
        return conn.execute(
            """
            update pets set name = coalesce(%s, name), updated_at = now()
            where id = %s returning *
            """,
            (name, pet_id),
        ).fetchone()


def add_consents(user_id: UUID, pet_id: UUID, items: list[ConsentItem], version: str) -> None:
    with get_pool().connection() as conn, conn.cursor() as cur:
        cur.executemany(
            "insert into consents (user_id, pet_id, item, version) values (%s, %s, %s, %s)",
            [(user_id, pet_id, item, version) for item in dict.fromkeys(items)],
        )


def latest_consents(pet_id: UUID) -> list[dict]:
    """항목별 가장 최근 동의."""
    with get_pool().connection() as conn:
        return conn.execute(
            """
            select distinct on (item) item, version, agreed_at from consents
            where pet_id = %s order by item, agreed_at desc
            """,
            (pet_id,),
        ).fetchall()
