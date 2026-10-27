"""PostgreSQL 작업 큐 (supabase/migrations/0001_jobs.sql).

API는 enqueue/get_job만 쓰고, 작업자는 claim → progress → complete/fail 순서로 쓴다.
"""
from typing import Any
from uuid import UUID

from psycopg.types.json import Jsonb

from app.db import get_pool
from app.schemas import JobType


def enqueue(job_type: JobType, user_id: UUID, payload: dict[str, Any],
            pet_id: UUID | None = None, idempotency_key: str | None = None) -> UUID:
    """같은 사용자가 같은 idempotency_key로 다시 요청하면 기존 작업 ID를 돌려준다."""
    with get_pool().connection() as conn:
        row = conn.execute(
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


def progress(job_id: UUID, percent: int, lease_seconds: int) -> None:
    """진행률을 갱신하면서 lease도 연장한다 (heartbeat)."""
    with get_pool().connection() as conn:
        conn.execute(
            """
            update jobs set progress = %s, updated_at = now(),
                   lease_expires_at = now() + make_interval(secs => %s)
            where id = %s
            """,
            (max(0, min(100, percent)), lease_seconds, job_id),
        )


def complete(job_id: UUID, result: dict[str, Any]) -> None:
    with get_pool().connection() as conn:
        conn.execute(
            """
            update jobs set status = 'succeeded', progress = 100, result = %s,
                   locked_by = null, lease_expires_at = null,
                   updated_at = now(), finished_at = now()
            where id = %s
            """,
            (Jsonb(result), job_id),
        )


def fail(job_id: UUID, code: str, message: str, retry: bool) -> None:
    """retry=True이고 시도 횟수가 남아 있으면 다시 대기열로 돌린다."""
    with get_pool().connection() as conn:
        conn.execute(
            """
            update jobs set
                status = case when %s and attempts < max_attempts then 'queued' else 'failed' end,
                finished_at = case when %s and attempts < max_attempts then null else now() end,
                error_code = %s, error_message = %s,
                locked_by = null, lease_expires_at = null, updated_at = now()
            where id = %s
            """,
            (retry, retry, code, message[:1000], job_id),
        )


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
