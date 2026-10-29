"""작업 큐 테스트. 로컬 DB(docker compose up -d)가 떠 있을 때만 실행된다."""
import uuid

import psycopg
import pytest

from app import queue
from app.config import get_settings
from app.schemas import JobType
from app.worker import main as worker


def _db_available() -> bool:
    try:
        psycopg.connect(get_settings().database_url, connect_timeout=1).close()
        return True
    except psycopg.OperationalError:
        return False


pytestmark = pytest.mark.skipif(not _db_available(), reason="로컬 DB 없음 (docker compose up -d)")


@pytest.fixture
def user():
    return uuid.uuid4()


def test_idempotency_returns_same_job(user):
    a = queue.enqueue(JobType.ECHO, user, {"n": 1}, idempotency_key="k1")
    b = queue.enqueue(JobType.ECHO, user, {"n": 2}, idempotency_key="k1")
    assert a == b


def test_other_user_cannot_read_job(user):
    job_id = queue.enqueue(JobType.ECHO, user, {})
    assert queue.get_job(job_id, uuid.uuid4()) is None


def test_worker_runs_echo(user):
    job_id = queue.enqueue(JobType.ECHO, user, {"hello": "world"})
    while worker.run_once():
        if queue.get_job(job_id, user)["status"] == "succeeded":
            break
    job = queue.get_job(job_id, user)
    assert job["status"] == "succeeded"
    assert job["result"] == {"echo": {"hello": "world"}}


def test_not_implemented_fails_without_retry(user):
    job_id = queue.enqueue(JobType.RIG, user, {})
    while worker.run_once():
        if queue.get_job(job_id, user)["status"] == "failed":
            break
    job = queue.get_job(job_id, user)
    assert job["status"] == "failed"
    assert job["error_code"] == "NOT_IMPLEMENTED"
    assert job["attempts"] == 1


@pytest.fixture
def job_type():
    """다른 테스트가 남긴 작업을 가져가지 않도록 테스트마다 고유한 종류를 쓴다."""
    return f"test-{uuid.uuid4()}"


def test_expired_lease_is_reclaimed(user, job_type):
    job_id = queue.enqueue(job_type, user, {})
    assert queue.claim("A", [job_type], 0)["id"] == job_id

    job = queue.claim("B", [job_type], 60)
    assert job["id"] == job_id
    assert job["locked_by"] == "B"
    assert job["attempts"] == 2


def test_stale_worker_cannot_overwrite(user, job_type):
    job_id = queue.enqueue(job_type, user, {})
    queue.claim("A", [job_type], 0)
    queue.claim("B", [job_type], 60)

    assert not queue.progress(job_id, "A", 50, 60)
    assert not queue.fail(job_id, "A", "INTERNAL", "늦게 온 실패", retry=False)
    assert not queue.complete(job_id, "A", {"by": "A"})
    assert queue.complete(job_id, "B", {"by": "B"})

    job = queue.get_job(job_id, user)
    assert job["status"] == "succeeded"
    assert job["result"] == {"by": "B"}


def test_reap_expired_fails_exhausted_job(user, job_type):
    job_id = queue.enqueue(job_type, user, {})
    for _ in range(3):
        assert queue.claim("A", [job_type], 0)["id"] == job_id
    assert queue.claim("A", [job_type], 0) is None

    queue.reap_expired()
    job = queue.get_job(job_id, user)
    assert job["status"] == "failed"
    assert job["error_code"] == "LEASE_EXPIRED"


def test_worker_stops_when_lease_lost(user, job_type, monkeypatch):
    from app.db import get_pool

    reached = []

    def handler(ctx):
        # 처리 도중 lease가 끝나 다른 작업자가 가져간 상황
        with get_pool().connection() as conn:
            conn.execute("update jobs set locked_by = 'B' where id = %s", (ctx.job["id"],))
        ctx.progress(50)
        reached.append(True)
        return {}

    monkeypatch.setattr(worker, "HANDLERS", {job_type: handler})
    job_id = queue.enqueue(job_type, user, {})
    assert worker.run_once()

    assert not reached
    job = queue.get_job(job_id, user)
    assert job["status"] == "running"
    assert job["locked_by"] == "B"
