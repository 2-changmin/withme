"""캐릭터 생성 API 실제 연결 (#23). 로컬 DB + 임시 폴더 local 저장소로 돈다.

작업자 처리 함수는 부르지 않고, 작업이 끝난 것처럼 결과를 써 넣은 뒤 pipeline 후처리를 부른다.
"""
import os

os.environ.update(ENV="local", AUTH_DEV_BYPASS="true", MOCK_CHARACTER="true")

import uuid

import pytest
from fastapi.testclient import TestClient
from psycopg.types.json import Jsonb

from app import media, pipeline
from app.api.main import app
from app.api.routes.character import SAMPLE_JOINTS
from app.config import get_settings
from app.db import get_pool

pytestmark = pytest.mark.db
client = TestClient(app)


@pytest.fixture(autouse=True)
def real_mode(storage, monkeypatch):
    monkeypatch.setattr(get_settings(), "mock_character", False)


def _user() -> dict:
    return {"X-Dev-User": str(uuid.uuid4())}


def _pet(headers, consents=("gemini_transfer", "meshy_transfer")) -> str:
    pet = client.post("/pets", headers=headers, json={"name": "콩이"}).json()["id"]
    if consents:
        client.post(f"/pets/{pet}/consents", headers=headers,
                    json={"items": list(consents), "version": "test"})
    return pet


def _upload(headers, pet) -> str:
    t = client.post(f"/pets/{pet}/uploads", headers=headers,
                    json={"mimeType": "image/jpeg", "sizeBytes": 3}).json()
    client.put(t["uploadUrl"], content=b"abc", headers=t["headers"])
    client.post(f"/media/{t['mediaId']}/finalize", headers=headers)
    return t["mediaId"]


def _job(job_id) -> dict:
    with get_pool().connection() as conn:
        return conn.execute("select * from jobs where id = %s", (job_id,)).fetchone()


def _finish(job_id, result: dict) -> None:
    """작업자가 작업을 끝낸 것처럼 결과를 쓰고 후처리를 부른다."""
    with get_pool().connection() as conn:
        conn.execute("update jobs set status = 'succeeded', progress = 100, result = %s "
                     "where id = %s", (Jsonb(result), job_id))
    pipeline.on_succeeded(_job(job_id), result)


def _generated(headers, pet, mime="image/png") -> str:
    user = headers["X-Dev-User"]
    return media.save_generated(user, pet, b"img", mime, source_type="generated_visual")


@pytest.fixture
def owner():
    return _user()


@pytest.fixture
def pet(owner):
    return _pet(owner)


def _create(owner, pet, sources, key=None):
    headers = {**owner, **({"Idempotency-Key": key} if key else {})}
    return client.post(f"/pets/{pet}/reference-views", headers=headers,
                       json={"sourceMediaIds": sources})


def _ready_views(owner, pet) -> tuple[str, str]:
    """4면이 다 만들어진 reference_view. (referenceViewId, jobId)"""
    job_id = _create(owner, pet, [_upload(owner, pet)]).json()["jobId"]
    rv_id = _job(job_id)["payload"]["referenceViewId"]
    _finish(job_id, {"referenceViewId": rv_id,
                     "viewMediaIds": [_generated(owner, pet) for _ in range(4)], "problems": {}})
    return rv_id, job_id


# ---- 4면 생성 ----

def test_create_needs_gemini_consent(owner):
    pet = _pet(owner, consents=("meshy_transfer",))
    r = _create(owner, pet, [_upload(owner, pet)])
    assert r.status_code == 403 and r.json()["error"]["code"] == "FORBIDDEN"


def test_create_rejects_photos_of_other_pet(owner, pet):
    other = _user()
    other_photo = _upload(other, _pet(other))
    for sources in ([other_photo], [str(uuid.uuid4())]):
        r = _create(owner, pet, sources)
        assert r.status_code == 422 and r.json()["error"]["code"] == "VALIDATION_FAILED"


def test_create_on_other_users_pet_is_404(pet):
    other = _user()
    r = _create(other, pet, [str(uuid.uuid4())])
    assert r.status_code == 404


def test_create_enqueues_job_once_per_idempotency_key(owner, pet):
    photo = _upload(owner, pet)
    a = _create(owner, pet, [photo], key="k1")
    b = _create(owner, pet, [photo], key="k1")
    assert a.status_code == b.status_code == 202
    assert a.json()["jobId"] == b.json()["jobId"]

    job = _job(a.json()["jobId"])
    assert job["type"] == "reference_views"
    assert job["payload"]["sourceMediaIds"] == [photo] and job["payload"]["attempt"] == 1


def test_finished_views_come_back_as_urls(owner, pet):
    _, job_id = _ready_views(owner, pet)
    result = client.get(f"/jobs/{job_id}", headers=owner).json()["result"]
    assert len(result["viewMediaIds"]) == 4
    assert len(result["views"]) == 4 and all(u.startswith("http") for u in result["views"])


# ---- 재생성 ----

def test_retry_up_to_three_attempts(owner, pet):
    rv_id, _ = _ready_views(owner, pet)
    first = client.post(f"/reference-views/{rv_id}/retry", headers=owner)
    again = client.post(f"/reference-views/{rv_id}/retry", headers=owner)
    assert first.status_code == 202 and first.json() == again.json()  # 두 번 눌러도 한 번

    job2 = _job(first.json()["jobId"])
    assert job2["payload"]["attempt"] == 2
    rv2 = job2["payload"]["referenceViewId"]
    job3 = _job(client.post(f"/reference-views/{rv2}/retry", headers=owner).json()["jobId"])
    assert job3["payload"]["attempt"] == 3

    r = client.post(f"/reference-views/{job3['payload']['referenceViewId']}/retry", headers=owner)
    assert r.status_code == 429 and r.json()["error"]["code"] == "LIMIT_EXCEEDED"


# ---- 승인 → 3D 생성 ----

def test_approve_needs_meshy_consent(owner):
    pet = _pet(owner, consents=("gemini_transfer",))
    rv_id, _ = _ready_views(owner, pet)
    r = client.post(f"/reference-views/{rv_id}/approve", headers=owner)
    assert r.status_code == 403 and r.json()["error"]["code"] == "FORBIDDEN"
    with get_pool().connection() as conn:
        assert conn.execute("select count(*) as n from character_models where reference_view_id"
                            " = %s", (rv_id,)).fetchone()["n"] == 0


def test_approve_before_views_ready_is_409(owner, pet):
    job_id = _create(owner, pet, [_upload(owner, pet)]).json()["jobId"]
    rv_id = _job(job_id)["payload"]["referenceViewId"]
    r = client.post(f"/reference-views/{rv_id}/approve", headers=owner)
    assert r.status_code == 409 and r.json()["error"]["code"] == "CONFLICT"


def test_approve_enqueues_meshy_once(owner, pet):
    rv_id, _ = _ready_views(owner, pet)
    a = client.post(f"/reference-views/{rv_id}/approve", headers=owner)
    b = client.post(f"/reference-views/{rv_id}/approve", headers=owner)
    assert a.status_code == 202 and a.json() == b.json()  # 중복 청구 없음

    job = _job(a.json()["jobId"])
    assert job["type"] == "meshy_generate"
    assert set(job["payload"]) == {"characterModelId", "petId", "viewMediaIds"}
    assert len(job["payload"]["viewMediaIds"]) == 4

    r = client.post(f"/reference-views/{rv_id}/retry", headers=owner)
    assert r.status_code == 409  # 확정한 4면은 재생성하지 않는다


def test_other_user_cannot_touch_reference_views(owner, pet):
    rv_id, _ = _ready_views(owner, pet)
    other = _user()
    for action in ("retry", "approve"):
        r = client.post(f"/reference-views/{rv_id}/{action}", headers=other)
        assert r.status_code == 404 and r.json()["error"]["code"] == "NOT_FOUND"


# ---- 메시 정리 → 관절 → 리깅 ----

def test_pipeline_meshy_to_rig(owner, pet):
    rv_id, _ = _ready_views(owner, pet)
    meshy_job = client.post(f"/reference-views/{rv_id}/approve", headers=owner).json()["jobId"]
    cm_id = _job(meshy_job)["payload"]["characterModelId"]

    assert client.get(f"/character-models/{cm_id}/joints", headers=owner).status_code == 409
    joints = SAMPLE_JOINTS.model_dump(mode="json", by_alias=True)
    assert client.post(f"/character-models/{cm_id}/rig", headers=owner,
                       json=joints).status_code == 409

    # Meshy 완료 → mesh_prepare가 자동 등록된다
    raw = _generated(owner, pet, "model/gltf-binary")
    _finish(meshy_job, {"characterModelId": cm_id, "rawGlbMediaId": raw})
    with get_pool().connection() as conn:
        cm = conn.execute("select * from character_models where id = %s", (cm_id,)).fetchone()
    prepare = _job(cm["job_id"])
    assert str(cm["raw_glb_media_id"]) == raw
    assert prepare["type"] == "mesh_prepare" and prepare["payload"]["rawGlbMediaId"] == raw
    assert client.get(f"/jobs/{meshy_job}", headers=owner).json()["result"]["glbUrl"]

    # 메시 정리 완료 → 자동 관절 조회 → 리깅 등록
    prepared = _generated(owner, pet, "model/gltf-binary")
    _finish(prepare["id"], {"characterModelId": cm_id, "preparedGlbMediaId": prepared,
                            "jointsAuto": joints})
    assert client.get(f"/character-models/{cm_id}/joints", headers=owner).json() == joints

    r = client.post(f"/character-models/{cm_id}/rig", headers=owner, json=joints)
    assert r.status_code == 202
    rig_job = _job(r.json()["jobId"])
    assert rig_job["type"] == "rig" and rig_job["payload"]["preparedGlbMediaId"] == prepared

    # 다른 사용자는 관절·리깅에 접근할 수 없다
    other = _user()
    assert client.get(f"/character-models/{cm_id}/joints", headers=other).status_code == 404


def test_late_result_of_replaced_job_is_ignored(owner, pet):
    """관절을 다시 지정해 리깅을 새로 등록했으면, 예전 리깅 결과는 행에 쓰지 않는다."""
    rv_id, _ = _ready_views(owner, pet)
    meshy_job = client.post(f"/reference-views/{rv_id}/approve", headers=owner).json()["jobId"]
    cm_id = _job(meshy_job)["payload"]["characterModelId"]
    with get_pool().connection() as conn:
        conn.execute("update character_models set prepared_glb_media_id = %s where id = %s",
                     (_generated(owner, pet, "model/gltf-binary"), cm_id))
    joints = SAMPLE_JOINTS.model_dump(mode="json", by_alias=True)
    old = client.post(f"/character-models/{cm_id}/rig", headers=owner, json=joints).json()["jobId"]
    client.post(f"/character-models/{cm_id}/rig", headers=owner, json=joints)

    _finish(old, {"characterModelId": cm_id,
                  "riggedGlbMediaId": _generated(owner, pet, "model/gltf-binary")})
    with get_pool().connection() as conn:
        cm = conn.execute("select * from character_models where id = %s", (cm_id,)).fetchone()
    assert cm["rigged_glb_media_id"] is None
