"""나의 방 물건 API·후처리 (#76). 로컬 DB + 임시 폴더 local 저장소로 돈다.

작업자 처리 함수는 부르지 않고, 작업이 끝난 것처럼 결과를 써 넣은 뒤 pipeline 후처리를 부른다.
"""
import os

os.environ.update(ENV="local", AUTH_DEV_BYPASS="true")

import uuid

import pytest
from fastapi.testclient import TestClient
from psycopg.types.json import Jsonb

from app import media, pipeline
from app.api.main import app
from app.db import get_pool

pytestmark = pytest.mark.db
client = TestClient(app)


@pytest.fixture(autouse=True)
def _storage(storage):
    return storage


@pytest.fixture
def owner():
    return {"X-Dev-User": str(uuid.uuid4())}


@pytest.fixture
def pet(owner):
    return client.post("/pets", headers=owner, json={"name": "콩이"}).json()["id"]


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
    with get_pool().connection() as conn:
        conn.execute("update jobs set status = 'succeeded', progress = 100, result = %s "
                     "where id = %s", (Jsonb(result), job_id))
    pipeline.on_succeeded(_job(job_id), result)


def _generated(owner, pet, mime="image/png") -> str:
    return media.save_generated(owner["X-Dev-User"], pet, b"img", mime,
                                source_type="generated_visual")


def _create(owner, pet, kind="toy", source=None, key=None):
    headers = {**owner, **({"Idempotency-Key": key} if key else {})}
    return client.post(f"/pets/{pet}/items", headers=headers,
                       json={"kind": kind, "sourceMediaId": source or _upload(owner, pet)})


def _cut(owner, pet, kind="toy") -> str:
    """누끼까지 끝난 물건. itemId"""
    body = _create(owner, pet, kind).json()
    _finish(body["jobId"], {"itemId": body["itemId"], "cutoutMediaId": _generated(owner, pet)})
    return body["itemId"]


def _items(owner, pet, **params) -> list[dict]:
    return client.get(f"/pets/{pet}/items", headers=owner, params=params).json()


def _gacha(owner, pet, key=None):
    headers = {**owner, **({"Idempotency-Key": key} if key else {})}
    return client.post(f"/pets/{pet}/toys/gacha", headers=headers)


# ---- 등록 → 누끼 ----

def test_create_enqueues_cutout_once_per_key(owner, pet):
    photo = _upload(owner, pet)
    a = _create(owner, pet, source=photo, key="k1")
    b = _create(owner, pet, source=photo, key="k1")
    assert a.status_code == 202 and a.json() == b.json()

    job = _job(a.json()["jobId"])
    assert job["type"] == "item_cutout"
    assert job["payload"] == {"itemId": a.json()["itemId"], "petId": pet, "sourceMediaId": photo}
    assert [i["status"] for i in _items(owner, pet)] == ["cutting"]


def test_create_rejects_other_pets_photo_and_other_users_pet(owner, pet):
    other = {"X-Dev-User": str(uuid.uuid4())}
    other_pet = client.post("/pets", headers=other, json={"name": "두부"}).json()["id"]
    r = _create(owner, pet, source=_upload(other, other_pet))
    assert r.status_code == 422 and r.json()["error"]["code"] == "VALIDATION_FAILED"
    assert _create(other, pet, source=str(uuid.uuid4())).status_code == 404
    assert client.get(f"/pets/{pet}/items", headers=other).status_code == 404


def test_toy_waits_for_gacha_after_cutout(owner, pet):
    _cut(owner, pet, "toy")
    [toy] = _items(owner, pet)
    assert toy["status"] == "cutout" and toy["cutoutUrl"].startswith("http")
    assert toy["glbUrl"] is None
    assert _job(toy["jobId"])["type"] == "item_cutout"  # 다음 작업이 등록되지 않음


@pytest.mark.parametrize("kind", ["snack", "bed"])
def test_snack_and_bed_go_straight_to_3d(owner, pet, kind):
    item_id = _cut(owner, pet, kind)
    [item] = _items(owner, pet, kind=kind)
    assert item["status"] == "generating"
    job = _job(item["jobId"])
    assert job["type"] == "item_views"
    assert job["payload"]["itemId"] == item_id and job["payload"]["kind"] == kind


# ---- 가챠 ----

def test_gacha_needs_four_candidates(owner, pet):
    for _ in range(3):
        _cut(owner, pet)
    _create(owner, pet)        # 누끼 중인 장난감은 후보가 아니다
    _cut(owner, pet, "snack")  # 간식도 후보가 아니다
    r = _gacha(owner, pet)
    assert r.status_code == 409 and r.json()["error"]["code"] == "CONFLICT"


def test_gacha_picks_one_without_repeats(owner, pet):
    toys = {_cut(owner, pet) for _ in range(4)}
    a = _gacha(owner, pet, key="g1")
    assert a.status_code == 202
    assert _gacha(owner, pet, key="g1").json() == a.json()  # 같은 키 = 같은 결과

    picked = a.json()["itemId"]
    assert picked in toys
    job = _job(a.json()["jobId"])
    assert job["type"] == "item_views" and job["payload"]["itemId"] == picked
    status = {i["id"]: i["status"] for i in _items(owner, pet)}
    assert status[picked] == "generating"
    assert sorted(status.values()) == ["cutout"] * 3 + ["generating"]

    assert _gacha(owner, pet, key="g2").status_code == 409  # 남은 후보 3개


# ---- 4면 → 3D ----

def test_views_then_meshy_then_ready(owner, pet):
    item_id = _cut(owner, pet, "snack")
    views_job = _items(owner, pet)[0]["jobId"]
    view_ids = [_generated(owner, pet) for _ in range(4)]
    _finish(views_job, {"itemId": item_id, "viewMediaIds": view_ids})

    [item] = _items(owner, pet)
    meshy = _job(item["jobId"])
    assert meshy["type"] == "meshy_generate"
    assert meshy["payload"] == {"itemId": item_id, "petId": pet, "itemKind": "snack",
                                "viewMediaIds": view_ids}

    glb = _generated(owner, pet, "model/gltf-binary")
    _finish(meshy["id"], {"itemId": item_id, "rawGlbMediaId": glb})
    [item] = _items(owner, pet)
    assert item["status"] == "ready" and item["glbUrl"].startswith("http")
    assert _job(item["jobId"])["type"] == "meshy_generate"  # 물건은 관절·리깅 단계가 없다
