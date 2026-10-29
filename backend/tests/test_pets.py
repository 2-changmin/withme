"""반려견 프로필·동의 API (#21). 로컬 DB에서 실제 테이블로 돈다."""
import os

os.environ.update(ENV="local", AUTH_DEV_BYPASS="true", MOCK_CHARACTER="true")

import uuid

import pytest
from fastapi.testclient import TestClient

from app.api.main import app

pytestmark = pytest.mark.db
client = TestClient(app)


def _user() -> dict:
    return {"X-Dev-User": str(uuid.uuid4())}


@pytest.fixture
def owner():
    return _user()


@pytest.fixture
def pet(owner):
    r = client.post("/pets", headers=owner, json={"name": "  콩이  "})
    assert r.status_code == 201
    return r.json()


def test_create_and_list(owner, pet):
    assert pet["name"] == "콩이" and pet["photoMediaId"] is None
    assert [p["id"] for p in client.get("/pets", headers=owner).json()] == [pet["id"]]


def test_one_pet_per_account(owner, pet):
    r = client.post("/pets", headers=owner, json={"name": "두부"})
    assert r.status_code == 409 and r.json()["error"]["code"] == "CONFLICT"


@pytest.mark.parametrize("name", ["", "   ", "가" * 31])
def test_invalid_name(owner, name):
    r = client.post("/pets", headers=owner, json={"name": name})
    assert r.status_code == 422 and r.json()["error"]["code"] == "VALIDATION_FAILED"


def test_update_keeps_missing_fields(owner, pet):
    r = client.patch(f"/pets/{pet['id']}", headers=owner, json={"name": "콩순이"})
    assert r.status_code == 200 and r.json()["name"] == "콩순이"
    r = client.patch(f"/pets/{pet['id']}", headers=owner, json={})
    assert r.json()["name"] == "콩순이"


def test_other_user_cannot_see_or_change_pet(pet):
    """T09: 남의 pet_id는 없는 것과 같은 404."""
    other = _user()
    pid = pet["id"]
    for r in (
        client.get(f"/pets/{pid}", headers=other),
        client.patch(f"/pets/{pid}", headers=other, json={"name": "x"}),
    ):
        assert r.status_code == 404 and r.json()["error"]["code"] == "NOT_FOUND"
    assert client.get("/pets", headers=other).json() == []
