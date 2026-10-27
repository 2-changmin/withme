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
