"""PostgreSQL 작업 큐 (supabase/migrations/0001_jobs.sql).

API는 enqueue/get_job만 쓰고, 작업자는 claim → progress → complete/fail 순서로 쓴다.
"""
from contextlib import nullcontext
from typing import Any
from uuid import UUID

import psycopg
from psycopg.types.json import Jsonb

from app.db import get_pool
from app.schemas import JobType


def enqueue(job_type: JobType, user_id: UUID, payload: dict[str, Any],
            pet_id: UUID | None = None, idempotency_key: str | None = None,
            conn: psycopg.Connection | None = None) -> UUID:
    """같은 사용자가 같은 idempotency_key로 다시 요청하면 기존 작업 ID를 돌려준다.

    conn을 주면 그 트랜잭션 안에서 등록한다 (작업이 가리키는 행과 함께 커밋되게).
    """
    with nullcontext(conn) if conn else get_pool().connection() as c:
        row = c.execute(
            """
            insert into jobs (type, user_id, pet_id, payload, idempotency_key)
            values (%s, %s, %s, %s, %s)
            on conflict (user_id, idempotency_key) do update set updated_at = jobs.updated_at
            returning id
            """,
            (job_type, user_id, pet_id, Jsonb(payload), idempotency_key),
        ).fetchone()
    return row["id"]


def get_job(job_id: UUID, user_id: UUID) -> dict | None:
    with get_pool().connection() as conn:
        return conn.execute(
            "select * from jobs where id = %s and user_id = %s", (job_id, user_id)
        ).fetchone()


def claim(worker_id: str, types: list[str], lease_seconds: int) -> dict | None:
    with get_pool().connection() as conn:
        return conn.execute(
            "select * from claim_job(%s, %s, %s)", (worker_id, types, lease_seconds)
        ).fetchone()


# progress/complete/fail은 지금 이 작업을 잡고 있는 작업자만 바꿀 수 있다.
# lease가 끝나 다른 작업자가 가져간 뒤 늦게 도착한 갱신은 무시하고 False를 돌려준다.
_OWNED = "id = %s and locked_by = %s and status = 'running'"


def progress(job_id: UUID, worker_id: str, percent: int, lease_seconds: int) -> bool:
    """진행률을 갱신하면서 lease도 연장한다 (heartbeat)."""
    with get_pool().connection() as conn:
        cur = conn.execute(
            f"""
            update jobs set progress = %s, updated_at = now(),
                   lease_expires_at = now() + make_interval(secs => %s)
            where {_OWNED}
            """,
            (max(0, min(100, percent)), lease_seconds, job_id, worker_id),
        )
        return cur.rowcount == 1


def complete(job_id: UUID, worker_id: str, result: dict[str, Any]) -> bool:
    with get_pool().connection() as conn:
        cur = conn.execute(
            f"""
            update jobs set status = 'succeeded', progress = 100, result = %s,
                   locked_by = null, lease_expires_at = null,
                   updated_at = now(), finished_at = now()
            where {_OWNED}
            """,
            (Jsonb(result), job_id, worker_id),
        )
        return cur.rowcount == 1


def fail(job_id: UUID, worker_id: str, code: str, message: str, retry: bool) -> bool:
    """retry=True이고 시도 횟수가 남아 있으면 다시 대기열로 돌린다."""
    with get_pool().connection() as conn:
        cur = conn.execute(
            f"""
            update jobs set
                status = case when %s and attempts < max_attempts then 'queued' else 'failed' end,
                finished_at = case when %s and attempts < max_attempts then null else now() end,
                error_code = %s, error_message = %s,
                locked_by = null, lease_expires_at = null, updated_at = now()
            where {_OWNED}
            """,
            (retry, retry, code, message[:1000], job_id, worker_id),
        )
        return cur.rowcount == 1


def reap_expired() -> int:
    """lease가 끝났고 재시도 횟수도 다 쓴 작업을 실패로 정리한다."""
    with get_pool().connection() as conn:
        cur = conn.execute(
            """
            update jobs set status = 'failed', error_code = 'LEASE_EXPIRED',
                   error_message = '작업자가 응답하지 않음', finished_at = now(), updated_at = now()
            where status = 'running' and lease_expires_at < now() and attempts >= max_attempts
            """
        )
        return cur.rowcount
