"""#18 인증·소유권·오류 규약 테스트. DB 없이 돈다 (소유권 조회는 바꿔 끼운다)."""
import os

os.environ.update(ENV="local", AUTH_DEV_BYPASS="true", MOCK_CHARACTER="true")

import time
import uuid

import jwt
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app import ownership
from app.auth import CurrentUser
from app.config import get_settings
from app.errors import install_error_handlers
from app.ownership import OwnedPet

SECRET = "test-secret-at-least-32-bytes-long!!"
USER = uuid.UUID("00000000-0000-0000-0000-000000000001")
OTHER = uuid.UUID("00000000-0000-0000-0000-000000000002")
PET = uuid.UUID("00000000-0000-0000-0000-0000000000aa")

app = FastAPI()
install_error_handlers(app)


@app.get("/me")
def me(user_id: CurrentUser) -> dict:
    return {"userId": str(user_id)}


@app.get("/pets/{pet_id}")
def pet(pet_id: OwnedPet) -> dict:
    return {"petId": str(pet_id)}


@app.get("/boom")
def boom() -> None:
    raise RuntimeError("select * from secret_table")


client = TestClient(app, raise_server_exceptions=False)


@pytest.fixture(autouse=True)
def hs256(monkeypatch):
    monkeypatch.setattr(get_settings(), "supabase_jwt_secret", SECRET)


@pytest.fixture
def owners(monkeypatch):
    """PET은 USER 소유로 둔다."""
    monkeypatch.setattr(ownership, "is_pet_owner",
                        lambda pet_id, user_id: (pet_id, user_id) == (PET, USER))


def _token(sub=USER, aud="authenticated", exp_in=3600, secret=SECRET) -> dict:
    claims = {"sub": str(sub), "aud": aud, "exp": int(time.time()) + exp_in}
    return {"Authorization": "Bearer " + jwt.encode(claims, secret, algorithm="HS256")}


def test_valid_token():
    r = client.get("/me", headers=_token())
    assert r.status_code == 200 and r.json() == {"userId": str(USER)}


@pytest.mark.parametrize("headers", [
    _token(exp_in=-10),
    _token(aud="anon"),
    _token(secret="other-secret-at-least-32-bytes-long!"),
    _token(sub="not-a-uuid"),
])
def test_rejected_tokens(headers):
    r = client.get("/me", headers=headers)
    assert r.status_code == 401
    assert r.json()["error"]["code"] == "UNAUTHORIZED"


def test_dev_user_must_be_uuid():
    r = client.get("/me", headers={"X-Dev-User": "abc"})
    assert r.status_code == 401
    assert r.json()["error"]["code"] == "UNAUTHORIZED"


def test_owner_can_access_pet(owners):
    r = client.get(f"/pets/{PET}", headers=_token())
    assert r.status_code == 200 and r.json() == {"petId": str(PET)}


def test_other_user_gets_same_404_as_missing_pet(owners):
    others = client.get(f"/pets/{PET}", headers=_token(sub=OTHER))
    missing = client.get(f"/pets/{uuid.uuid4()}", headers=_token())
    assert others.status_code == missing.status_code == 404
    assert others.json() == missing.json() == {
        "error": {"code": "NOT_FOUND", "message": "반려견이 없습니다"}}


def test_pet_id_checked_after_login(owners):
    assert client.get(f"/pets/{PET}").status_code == 401


def test_unhandled_error_hides_details():
    r = client.get("/boom")
    assert r.status_code == 500
    assert r.json() == {"error": {"code": "INTERNAL", "message": "서버 오류"}}


def test_http_errors_use_matching_code():
    assert client.get("/nope").json()["error"]["code"] == "NOT_FOUND"
