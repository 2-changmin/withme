import pytest

from app.config import Settings, get_settings
from app.worker.handlers import JobContext, meshy_generate


@pytest.fixture
def settings(monkeypatch):
    def use(**env):
        for key, value in env.items():
            monkeypatch.setenv(key, value)
        get_settings.cache_clear()

    yield use
    get_settings.cache_clear()


def _ctx(progress: list[int]) -> JobContext:
    job = {"payload": {"characterModelId": "cm-1", "viewUrls": ["https://example.com/side.png"]}}
    return JobContext(job=job, progress=progress.append)


def test_mock_returns_fake_glb_without_calling_meshy(settings):
    settings(MOCK_MESHY="true")
    progress: list[int] = []

    result = meshy_generate.handle(_ctx(progress))

    assert result == {"characterModelId": "cm-1", "rawGlbPath": meshy_generate.MOCK_RAW_GLB_PATH}
    assert progress[-1] == 100


def test_mock_is_default():
    # .env에 MOCK_MESHY가 없으면 목업이어야 pull 받은 사람이 크레딧을 쓰지 않는다
    assert Settings.model_fields["mock_meshy"].default is True
