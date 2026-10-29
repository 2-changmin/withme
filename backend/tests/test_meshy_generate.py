"""meshy_generate 작업 처리 함수. Meshy는 httpx.MockTransport로, 저장소·DB는 가짜로 바꿔 끼운다."""
import base64
import json
import struct

import httpx
import pytest

from app.config import Settings, get_settings
from app.errors import AppError, ErrorCode
from app.worker.handlers import JobContext, meshy_generate

API = meshy_generate.API
GLB = b"glTF-fake-model"


@pytest.fixture
def env(monkeypatch):
    """저장소·DB를 메모리로 바꾸고 Meshy 응답을 정해 준다."""
    saved: dict[str, bytes] = {}
    tasks: dict[str, str | None] = {}

    monkeypatch.setattr(meshy_generate.media, "load", lambda m, pet: (f"img-{m}".encode(), "image/png"))

    def save(user_id, pet_id, data, mime, source_type, parent_id=None):
        assert mime == "model/gltf-binary" and source_type == "generated_model"
        saved[f"glb-{len(saved)}"] = data
        return f"glb-{len(saved) - 1}"

    monkeypatch.setattr(meshy_generate.media, "save_generated", save)
    monkeypatch.setattr(meshy_generate, "_saved_task_id",
                        lambda ctx: tasks.get(ctx.payload.get("characterModelId")))
    monkeypatch.setattr(meshy_generate, "_remember_task",
                        lambda ctx, t: tasks.__setitem__(ctx.payload.get("characterModelId"), t))
    monkeypatch.setattr(meshy_generate, "_sleep", lambda s: None)

    def use(mock: bool, meshy=None, **settings):
        s = Settings(mock_meshy=mock, meshy_api_key="msy_test", **settings)
        monkeypatch.setattr(meshy_generate, "get_settings", lambda: s)
        seen: list[httpx.Request] = []
        if meshy:
            def record(req):
                seen.append(req)
                return meshy(req)
            monkeypatch.setattr(meshy_generate, "_transport", httpx.MockTransport(record))
        return seen

    use.saved, use.tasks = saved, tasks
    return use


def run(payload: dict) -> tuple[dict, list[int]]:
    progress: list[int] = []
    job = {"id": "job-1", "user_id": "user-1",
           "payload": {"petId": "pet-1", "viewMediaIds": ["s", "f", "b", "o"], **payload}}
    return meshy_generate.handle(JobContext(job, progress.append)), progress


def meshy(statuses=("PENDING", "IN_PROGRESS", "SUCCEEDED"), create=None, delete=200):
    """POST → GET(statuses 순서) → GLB 다운로드 → DELETE 를 흉내 낸다."""
    polls = iter(statuses)

    def handler(req: httpx.Request) -> httpx.Response:
        if req.url.host == "assets.meshy.ai":
            return httpx.Response(200, content=GLB)
        if req.method == "POST":
            return create(req) if create else httpx.Response(202, json={"result": "task-1"})
        if req.method == "DELETE":
            return httpx.Response(delete, json={})
        status = next(polls)
        return httpx.Response(200, json={
            "id": "task-1", "status": status, "progress": 100 if status == "SUCCEEDED" else 40,
            "model_urls": {"glb": "https://assets.meshy.ai/t/model.glb?Expires=1"},
            "task_error": {"message": "boom"} if status == "FAILED" else None,
        })

    return handler


def test_mock_is_default():
    # .env에 MOCK_MESHY가 없으면 목업이어야 pull 받은 사람이 크레딧을 쓰지 않는다
    assert Settings.model_fields["mock_meshy"].default is True


def test_mock_saves_valid_glb_so_pipeline_continues(env):
    seen = env(mock=True, meshy=lambda req: pytest.fail("목업인데 Meshy를 불렀다"))

    result, progress = run({"characterModelId": "cm-1"})

    assert result == {"characterModelId": "cm-1", "rawGlbMediaId": "glb-0", "meshyTaskId": None}
    glb = env.saved["glb-0"]
    magic, version, length = struct.unpack_from("<4sII", glb)
    assert (magic, version, length) == (b"glTF", 2, len(glb))
    assert progress[-1] == 100 and seen == []


def test_mock_uses_given_glb_file(env, tmp_path):
    sample = tmp_path / "dog.glb"
    sample.write_bytes(b"real-dog")
    env(mock=True, mock_meshy_glb=str(sample))

    run({"characterModelId": "cm-1"})

    assert env.saved["glb-0"] == b"real-dog"


def test_generates_saves_and_deletes_meshy_task(env):
    seen = env(mock=False, meshy=meshy())

    result, progress = run({"characterModelId": "cm-1"})

    assert result == {"characterModelId": "cm-1", "rawGlbMediaId": "glb-0", "meshyTaskId": "task-1"}
    assert env.saved["glb-0"] == GLB
    assert env.tasks["cm-1"] == "task-1"

    create = json.loads(seen[0].read())
    assert seen[0].headers["authorization"] == "Bearer msy_test"
    assert create["target_polycount"] == meshy_generate.CHARACTER_POLYCOUNT
    assert create["image_urls"][0] == "data:image/png;base64," + base64.b64encode(b"img-s").decode()
    assert len(create["image_urls"]) == 4

    download = next(r for r in seen if r.url.host == "assets.meshy.ai")
    assert "authorization" not in download.headers  # 서명 URL에 키를 보내지 않는다
    assert (seen[-1].method, str(seen[-1].url)) == ("DELETE", f"{API}/task-1")
    assert progress == sorted(progress) and progress[0] == 5 and progress[-1] == 100


def test_item_uses_item_polycount(env):
    seen = env(mock=False, meshy=meshy())

    result, _ = run({"itemId": "item-1", "itemKind": "snack"})

    assert result == {"itemId": "item-1", "rawGlbMediaId": "glb-0", "meshyTaskId": "task-1"}
    assert json.loads(seen[0].read())["target_polycount"] == meshy_generate.ITEM_POLYCOUNT


def test_retry_resumes_existing_task_without_new_charge(env):
    seen = env(mock=False, meshy=meshy(statuses=("IN_PROGRESS", "IN_PROGRESS", "SUCCEEDED")))
    env.tasks["cm-1"] = "task-1"  # 이전 시도에서 만든 작업

    result, _ = run({"characterModelId": "cm-1"})

    assert result["meshyTaskId"] == "task-1"
    assert not any(r.method == "POST" for r in seen)


def test_meshy_failed_is_retried_with_new_task(env):
    env(mock=False, meshy=meshy(statuses=("IN_PROGRESS", "FAILED")))

    with pytest.raises(AppError) as e:
        run({"characterModelId": "cm-1"})

    assert e.value.code == ErrorCode.UPSTREAM_FAILED and "boom" in e.value.message
    assert env.tasks["cm-1"] is None  # 다음 시도는 새 작업을 만든다
    assert env.saved == {}


@pytest.mark.parametrize("status,code", [
    (429, ErrorCode.UPSTREAM_FAILED),
    (503, ErrorCode.UPSTREAM_FAILED),
    (402, ErrorCode.LIMIT_EXCEEDED),
    (400, ErrorCode.VALIDATION_FAILED),
    (401, ErrorCode.INTERNAL),
])
def test_http_errors_map_to_queue_rules(env, status, code):
    env(mock=False, meshy=meshy(create=lambda req: httpx.Response(status, json={"message": "x"})))

    with pytest.raises(AppError) as e:
        run({"characterModelId": "cm-1"})

    assert e.value.code == code


def test_network_error_is_retried(env):
    def broken(req):
        raise httpx.ConnectError("connection aborted", request=req)

    env(mock=False, meshy=broken)

    with pytest.raises(AppError) as e:
        run({"characterModelId": "cm-1"})

    assert e.value.code == ErrorCode.UPSTREAM_FAILED


def test_delete_failure_does_not_fail_job(env):
    env(mock=False, meshy=meshy(delete=500))

    result, _ = run({"characterModelId": "cm-1"})

    assert result["rawGlbMediaId"] == "glb-0"


def test_lease_lost_is_not_swallowed(env):
    env(mock=False, meshy=meshy())

    class LeaseLost(Exception):
        pass

    def progress(percent):
        if percent > 5:
            raise LeaseLost

    job = {"id": "job-1", "user_id": "u", "payload": {
        "petId": "p", "characterModelId": "cm-1", "viewMediaIds": ["s"]}}
    with pytest.raises(LeaseLost):
        meshy_generate.handle(JobContext(job, progress))


@pytest.mark.parametrize("views", [[], ["a", "b", "c", "d", "e"]])
def test_rejects_wrong_view_count(env, views):
    env(mock=True)

    with pytest.raises(AppError) as e:
        run({"characterModelId": "cm-1", "viewMediaIds": views})

    assert e.value.code == ErrorCode.VALIDATION_FAILED


def test_settings_cache_untouched():
    # 다른 테스트가 get_settings를 바꿔 끼워도 실제 설정은 그대로다
    assert get_settings().mock_meshy is True
