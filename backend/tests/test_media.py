"""미디어 업로드 API (#30). 로컬 DB + 임시 폴더 local 저장소로 돈다."""
import os

os.environ.update(ENV="local", AUTH_DEV_BYPASS="true", MOCK_CHARACTER="true")

import time
import uuid

import pytest
from fastapi.testclient import TestClient

from app import media, queue
from app.api.main import app

pytestmark = pytest.mark.db
client = TestClient(app)
MB = media.MB


def _user() -> dict:
    return {"X-Dev-User": str(uuid.uuid4())}


@pytest.fixture
def owner(storage):
    return _user()


@pytest.fixture
def pet(owner):
    return client.post("/pets", headers=owner, json={"name": "콩이"}).json()["id"]


def ticket(owner, pet, mime="image/jpeg", size=3):
    return client.post(f"/pets/{pet}/uploads", headers=owner,
                       json={"mimeType": mime, "sizeBytes": size})


def upload(owner, pet, data=b"abc", mime="image/jpeg") -> str:
    t = ticket(owner, pet, mime, len(data)).json()
    assert client.put(t["uploadUrl"], content=data, headers=t["headers"]).status_code == 200
    return t["mediaId"]


def test_upload_finalize_and_download(owner, pet):
    t = ticket(owner, pet)
    assert t.status_code == 201
    t = t.json()
    assert t["method"] == "PUT" and t["headers"] == {"Content-Type": "image/jpeg"}

    m = client.get(f"/media/{t['mediaId']}", headers=owner).json()
    assert m["status"] == "pending" and m["url"] is None

    client.put(t["uploadUrl"], content=b"abc", headers=t["headers"])
    r = client.post(f"/media/{t['mediaId']}/finalize", headers=owner)
    assert r.status_code == 202
    job = queue.get_job(uuid.UUID(r.json()["jobId"]), uuid.UUID(owner["X-Dev-User"]))
    assert job["type"] == "media_check" and job["payload"] == {"mediaId": t["mediaId"]}

    m = client.get(f"/media/{t['mediaId']}", headers=owner).json()
    assert m["status"] == "uploaded" and m["sizeBytes"] == 3 and m["petId"] == pet
    assert client.get(m["url"]).content == b"abc"


def test_finalize_twice_returns_same_job(owner, pet):
    mid = upload(owner, pet)
    a = client.post(f"/media/{mid}/finalize", headers=owner).json()
    b = client.post(f"/media/{mid}/finalize", headers=owner).json()
    assert a == b


def test_finalize_before_upload(owner, pet):
    mid = ticket(owner, pet).json()["mediaId"]
    r = client.post(f"/media/{mid}/finalize", headers=owner)
    assert r.status_code == 422


def test_finalize_rejects_file_larger_than_limit(owner, pet, storage):
    mid = ticket(owner, pet, size=1).json()["mediaId"]
    # 서명 URL로는 크기를 막을 수 없으니(Supabase) 큰 파일이 올라왔다고 가정한다
    storage.put(media.get(mid)["path"], b"x" * (10 * MB + 1), "image/jpeg")

    r = client.post(f"/media/{mid}/finalize", headers=owner)
    assert r.status_code == 429
    m = client.get(f"/media/{mid}", headers=owner).json()
    assert m["status"] == "rejected" and m["errorCode"] == "LIMIT_EXCEEDED"
    assert storage.size(media.get(mid)["path"]) is None


@pytest.mark.parametrize("mime,size", [("image/gif", 3), ("text/plain", 3),
                                       ("image/jpeg", 10 * MB + 1), ("video/mp4", 50 * MB + 1)])
def test_rejects_bad_type_or_size(owner, pet, mime, size):
    r = ticket(owner, pet, mime, size)
    assert r.status_code == 422 and r.json()["error"]["code"] == "VALIDATION_FAILED"


def test_photo_count_limit(owner, pet):
    for _ in range(10):
        assert ticket(owner, pet).status_code == 201
    r = ticket(owner, pet)
    assert r.status_code == 429 and r.json()["error"]["code"] == "LIMIT_EXCEEDED"
    # 다른 종류는 따로 센다
    assert ticket(owner, pet, "video/mp4").status_code == 201


def test_total_size_limit(owner, pet):
    for _ in range(3):
        assert ticket(owner, pet, "video/mp4", 50 * MB).status_code == 201
    for _ in range(2):
        assert ticket(owner, pet, "audio/mpeg", 20 * MB).status_code == 201
    # 영상 150MB + 소리 40MB, 사진 10MB까지는 200MB 안이고 그다음은 넘는다
    assert ticket(owner, pet, "image/jpeg", 10 * MB).status_code == 201
    r = ticket(owner, pet, "image/jpeg", 1)
    assert r.status_code == 429 and "용량" in r.json()["error"]["message"]


def test_other_user_cannot_see_or_upload(owner, pet):
    mid = upload(owner, pet)
    stranger = _user()
    assert client.get(f"/media/{mid}", headers=stranger).status_code == 404
    assert client.post(f"/media/{mid}/finalize", headers=stranger).status_code == 404
    assert ticket(stranger, pet).status_code == 404


def test_signed_url_tampered_or_expired(owner, pet, storage):
    t = ticket(owner, pet).json()
    path = media.get(t["mediaId"])["path"]

    tampered = t["uploadUrl"].replace(path, path.replace(".jpg", ".png"))
    assert client.put(tampered, content=b"abc").status_code == 403

    exp = int(time.time()) - 1
    expired = f"/storage/local/{path}?exp={exp}&sig={storage.sign('put', path, exp)}"
    assert client.put(expired, content=b"abc").status_code == 403

    # 업로드 서명으로 내려받을 수 없다
    assert client.get(t["uploadUrl"]).status_code == 403


def test_media_check_marks_ready(owner, pet):
    from app.worker.handlers import JobContext, media_check
    media_id = upload(owner, pet)
    client.post(f"/media/{media_id}/finalize", headers=owner)
    ctx = JobContext({"payload": {"mediaId": media_id}}, lambda _: None)
    assert media_check.handle(ctx) == {"mediaId": media_id, "status": "ready"}
    assert media_check.handle(ctx)["status"] == "ready"  # 다시 돌아도 그대로
    assert client.get(f"/media/{media_id}", headers=owner).json()["status"] == "ready"


def test_media_check_before_upload_fails(owner, pet):
    from app.errors import AppError, ErrorCode
    from app.worker.handlers import JobContext, media_check
    media_id = ticket(owner, pet).json()["mediaId"]
    with pytest.raises(AppError) as e:
        media_check.handle(JobContext({"payload": {"mediaId": media_id}}, lambda _: None))
    assert e.value.code == ErrorCode.CONFLICT
