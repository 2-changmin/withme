"""meshy_generate를 실제 DB·저장소로 돌려 다음 단계(mesh_prepare)까지 이어지는지 확인한다.

Meshy는 부르지 않는다 (MOCK_MESHY). 로컬 DB(docker compose up -d)가 있어야 돈다.
"""
import uuid

import pytest
from fastapi.testclient import TestClient

from app import media, pipeline
from app.api.main import app
from app.config import get_settings
from app.db import get_pool
from app.worker.handlers import JobContext, meshy_generate

pytestmark = pytest.mark.db
client = TestClient(app)


@pytest.fixture(autouse=True)
def real_api_mock_meshy(storage, monkeypatch):
    monkeypatch.setattr(get_settings(), "mock_character", False)
    monkeypatch.setattr(get_settings(), "mock_meshy", True)
    monkeypatch.setattr(get_settings(), "mock_meshy_glb", "")


def _job(job_id) -> dict:
    with get_pool().connection() as conn:
        return conn.execute("select * from jobs where id = %s", (job_id,)).fetchone()


def _approved_meshy_job() -> dict:
    """4면이 준비된 상태에서 보호자가 승인해 등록된 meshy_generate 작업."""
    owner = {"X-Dev-User": str(uuid.uuid4())}
    pet = client.post("/pets", headers=owner, json={"name": "콩이"}).json()["id"]
    user = owner["X-Dev-User"]
    source = media.save_generated(user, pet, b"photo", "image/jpeg", source_type="upload")
    rv_job = client.post(f"/pets/{pet}/reference-views", headers=owner,
                         json={"sourceMediaIds": [source]}).json()["jobId"]
    job = _job(rv_job)
    views = [media.save_generated(user, pet, b"view", "image/png", source_type="generated_visual")
             for _ in range(4)]
    pipeline.on_succeeded(job, {"referenceViewId": job["payload"]["referenceViewId"],
                                "viewMediaIds": views, "problems": {}})
    approved = client.post(f"/reference-views/{job['payload']['referenceViewId']}/approve",
                           headers=owner).json()["jobId"]
    return _job(approved)


def test_mock_result_flows_into_mesh_prepare():
    job = _approved_meshy_job()

    result = meshy_generate.handle(JobContext(job, lambda percent: None))
    pipeline.on_succeeded(job, result)

    glb, mime = media.load(result["rawGlbMediaId"], job["payload"]["petId"])
    assert mime == "model/gltf-binary" and glb[:4] == b"glTF"
    with get_pool().connection() as conn:
        cm = conn.execute("select * from character_models where id = %s",
                          (job["payload"]["characterModelId"],)).fetchone()
    assert str(cm["raw_glb_media_id"]) == result["rawGlbMediaId"]
    prepare = _job(cm["job_id"])
    assert prepare["type"] == "mesh_prepare"
    assert prepare["payload"]["rawGlbMediaId"] == result["rawGlbMediaId"]


def test_meshy_task_id_is_kept_for_retry():
    job = _approved_meshy_job()
    ctx = JobContext(job, lambda percent: None)

    meshy_generate._remember_task(ctx, "task-9")
    assert meshy_generate._saved_task_id(ctx) == "task-9"

    # 다른 작업(재생성 등)으로 바뀐 행에는 쓰지 않는다
    stale = JobContext({**job, "id": uuid.uuid4()}, lambda percent: None)
    meshy_generate._remember_task(stale, "task-old")
    assert meshy_generate._saved_task_id(ctx) == "task-9"
    assert meshy_generate._saved_task_id(stale) is None
