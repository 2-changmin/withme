"""무지개다리 편지 (#74). 로컬 DB로 돌고 Gemini는 부르지 않는다."""
import os

os.environ.update(ENV="local", AUTH_DEV_BYPASS="true", MOCK_CHARACTER="true")

import uuid
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient

from app import pipeline
from app.api.main import app
from app.config import get_settings
from app.db import get_pool
from app.worker import letter_writer
from app.worker.handlers import JobContext, letter_reply

client = TestClient(app)


def _user() -> dict:
    return {"X-Dev-User": str(uuid.uuid4())}


@pytest.fixture
def owner():
    return _user()


@pytest.fixture
def pet(owner):
    return client.post("/pets", headers=owner, json={"name": "콩이"}).json()["id"]


def _write(owner, pet, body="콩아, 산책하던 길을 지나가다 네 생각이 났어."):
    return client.post(f"/pets/{pet}/letters", headers=owner, json={"body": body})


def _run_job(job_id: str) -> None:
    """작업자가 처리한 것처럼 처리 함수 → complete → 후처리."""
    job = _job(job_id)
    result = letter_reply.handle(JobContext(job, lambda _: None))
    with get_pool().connection() as conn:
        conn.execute("update jobs set status = 'succeeded' where id = %s", (job["id"],))
    pipeline.on_succeeded(job, result)


def _job(job_id: str) -> dict:
    with get_pool().connection() as conn:
        return conn.execute("select * from jobs where id = %s", (job_id,)).fetchone()


@pytest.mark.db
def test_write_and_get_reply(owner, pet):
    r = _write(owner, pet)
    assert r.status_code == 201
    letter = r.json()
    assert letter["status"] == "pending" and letter["reply"] is None and letter["jobId"]
    assert letter["notice"] == "AI가 콩이의 모습을 떠올려 쓴 답장이에요."
    assert _job(letter["jobId"])["payload"] == {"letterId": letter["id"]}  # 본문은 작업에 싣지 않음

    _run_job(letter["jobId"])
    got = client.get(f"/letters/{letter['id']}", headers=owner).json()
    assert got["status"] == "replied" and got["reply"].startswith("나 콩이야.")
    assert got["repliedAt"] and got["help"] is None
    assert [x["id"] for x in client.get(f"/pets/{pet}/letters", headers=owner).json()] == [letter["id"]]


@pytest.mark.db
def test_one_letter_per_day(owner, pet):
    assert _write(owner, pet).status_code == 201
    r = _write(owner, pet, "또 쓰고 싶어")
    assert r.status_code == 429 and r.json()["error"]["code"] == "LIMIT_EXCEEDED"


@pytest.mark.db
def test_failed_reply_does_not_count(owner, pet):
    letter = _write(owner, pet).json()
    with get_pool().connection() as conn:
        conn.execute("update jobs set status = 'failed' where id = %s", (letter["jobId"],))
    assert client.get(f"/letters/{letter['id']}", headers=owner).json()["status"] == "failed"
    assert _write(owner, pet, "다시 써 볼게").status_code == 201


@pytest.mark.db
@pytest.mark.parametrize("body", ["너 없이 살기 싫어", "나도 너 따라 가고 싶어", "죽고싶다"])
def test_crisis_letter_gets_help_not_reply(owner, pet, body):
    r = _write(owner, pet, body)
    assert r.status_code == 201
    letter = r.json()
    assert letter["status"] == "crisis" and letter["jobId"] is None and letter["reply"] is None
    assert "109" in letter["help"]
    assert _write(owner, pet).status_code == 201  # 위기 편지는 하루 횟수에 세지 않음


@pytest.mark.db
def test_llm_crisis_result_sets_crisis(owner, pet, monkeypatch):
    letter = _write(owner, pet).json()
    monkeypatch.setattr(letter_writer, "write_reply", lambda *_: {"crisis": True, "reply": None})
    _run_job(letter["jobId"])
    got = client.get(f"/letters/{letter['id']}", headers=owner).json()
    assert got["status"] == "crisis" and got["reply"] is None and "109" in got["help"]


@pytest.mark.db
def test_other_user_cannot_read_or_delete(owner, pet):
    letter = _write(owner, pet).json()
    other = _user()
    for r in (client.get(f"/letters/{letter['id']}", headers=other),
              client.delete(f"/letters/{letter['id']}", headers=other),
              client.get(f"/pets/{pet}/letters", headers=other),
              _write(other, pet),
              client.get("/letters/not-a-uuid", headers=owner)):
        assert r.status_code == 404 and r.json()["error"]["code"] == "NOT_FOUND"


@pytest.mark.db
def test_delete(owner, pet):
    letter = _write(owner, pet).json()
    assert client.delete(f"/letters/{letter['id']}", headers=owner).status_code == 204
    assert client.get(f"/letters/{letter['id']}", headers=owner).status_code == 404


@pytest.mark.db
@pytest.mark.parametrize("body", ["", "   ", "가" * 2001])
def test_invalid_body(owner, pet, body):
    assert _write(owner, pet, body).status_code == 422


# ---- 답장 쓰기 (DB 없이) ----

def test_fallback_reply_particle():
    assert letter_writer.fallback_reply("콩이").startswith("나 콩이야.")
    assert letter_writer.fallback_reply("별").startswith("나 별이야.")
    assert letter_writer.fallback_reply("Max").startswith("나 Max야.")


@pytest.mark.parametrize("reply, safe", [
    ("편지 고마워. 늘 사랑해.", True),
    ("나중에 꼭 다시 만나자.", False),
    ("무지개다리에서 기다릴게.", False),
    ("그건 네 탓이 아니야.", True),
    ("매일 편지 써 줘.", False),
    ("", False),
])
def test_is_safe(reply, safe):
    assert letter_writer.is_safe(reply) is safe


class _FakeModels:
    def __init__(self, outputs):
        self.outputs = list(outputs)
        self.calls = 0

    def generate_content(self, **_):
        self.calls += 1
        return SimpleNamespace(text=self.outputs.pop(0))


@pytest.fixture
def real_gemini(monkeypatch):
    s = get_settings()
    monkeypatch.setattr(s, "mock_letter", False)
    monkeypatch.setattr(s, "gemini_api_key", "test")
    models = _FakeModels([])
    monkeypatch.setattr(letter_writer.genai, "Client",
                        lambda **_: SimpleNamespace(models=models))
    return models


def test_rewrites_once_then_falls_back(real_gemini):
    bad = '{"crisis": false, "reply": "다시 만나자"}'
    real_gemini.outputs = [bad, bad]
    out = letter_writer.write_reply("콩이", "보고 싶어")
    assert real_gemini.calls == 2 and out == {"crisis": False,
                                              "reply": letter_writer.fallback_reply("콩이")}


def test_uses_safe_reply_and_crisis(real_gemini):
    real_gemini.outputs = ['{"crisis": false, "reply": "편지 고마워."}',
                           '{"crisis": true, "reply": ""}']
    assert letter_writer.write_reply("콩이", "보고 싶어")["reply"] == "편지 고마워."
    assert letter_writer.write_reply("콩이", "...") == {"crisis": True, "reply": None}


def test_bad_json_is_upstream_error(real_gemini):
    from app.errors import AppError, ErrorCode
    real_gemini.outputs = ["not json"]
    with pytest.raises(AppError) as e:
        letter_writer.write_reply("콩이", "보고 싶어")
    assert e.value.code == ErrorCode.UPSTREAM_FAILED
