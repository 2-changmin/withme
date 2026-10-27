import os

os.environ.update(ENV="local", AUTH_DEV_BYPASS="true", MOCK_CHARACTER="true")

from fastapi.testclient import TestClient

from app.api.main import app
from app.api.routes.character import SAMPLE_JOINTS

client = TestClient(app)
USER = {"X-Dev-User": "00000000-0000-0000-0000-000000000001"}


def test_health():
    assert client.get("/health").json()["status"] == "ok"


def test_requires_token():
    r = client.get("/character-models/x/joints")
    assert r.status_code == 401
    assert r.json() == {"error": {"code": "UNAUTHORIZED", "message": "Bearer 토큰이 필요합니다"}}


def test_invalid_token():
    r = client.get("/character-models/x/joints", headers={"Authorization": "Bearer nope"})
    assert r.status_code == 401


def test_mock_flow_reference_views_to_rig():
    pet = "00000000-0000-0000-0000-0000000000aa"
    job_id = client.post(f"/pets/{pet}/reference-views", headers=USER).json()["jobId"]
    job = client.get(f"/jobs/{job_id}", headers=USER).json()
    assert job["status"] == "succeeded" and len(job["result"]["views"]) == 4

    job_id = client.post("/reference-views/rv/approve", headers=USER).json()["jobId"]
    assert client.get(f"/jobs/{job_id}", headers=USER).json()["type"] == "meshy_generate"

    joints = client.get("/character-models/cm/joints", headers=USER).json()
    assert joints["skeletonVersion"] == 1 and len(joints["joints"]) == 10

    r = client.post("/character-models/cm/rig", headers=USER, json=joints)
    assert r.status_code == 202
    assert client.get(f"/jobs/{r.json()['jobId']}", headers=USER).json()["status"] == "succeeded"


def test_rig_rejects_missing_joints():
    body = SAMPLE_JOINTS.model_dump(by_alias=True)
    del body["joints"]["nose"]
    r = client.post("/character-models/cm/rig", headers=USER, json=body)
    assert r.status_code == 422
    assert r.json()["error"]["code"] == "VALIDATION_FAILED"


def test_unknown_mock_job_is_404():
    r = client.get("/jobs/mock-unknown-1", headers=USER)
    assert r.status_code == 404
    assert r.json()["error"]["code"] == "NOT_FOUND"
