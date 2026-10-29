import os

import psycopg
import pytest

from app.config import get_settings


def _db_available() -> bool:
    try:
        psycopg.connect(get_settings().database_url, connect_timeout=1).close()
        return True
    except psycopg.OperationalError:
        return False


def pytest_configure(config):
    config.addinivalue_line("markers", "db: 로컬 DB(docker compose up -d)가 있어야 도는 테스트")


def pytest_collection_modifyitems(config, items):
    """`pytestmark = pytest.mark.db`인 테스트는 DB가 없으면 건너뛴다. CI는 postgres 서비스를 띄운다."""
    if _db_available():
        return
    if os.environ.get("CI"):
        raise pytest.UsageError("CI에서는 DB 테스트를 건너뛰지 않는다: DATABASE_URL 연결 실패")
    skip = pytest.mark.skip(reason="로컬 DB 없음 (docker compose up -d)")
    for item in items:
        if "db" in item.keywords:
            item.add_marker(skip)


@pytest.fixture
def storage(tmp_path, monkeypatch):
    """미디어 저장소를 임시 폴더의 local 저장소로 바꾼다."""
    from app import media
    from app.api.routes import media as media_routes
    from app.storage import LocalStorage

    s = LocalStorage(str(tmp_path), "http://testserver", "test-secret")
    monkeypatch.setattr(media, "get_storage", lambda: s)
    monkeypatch.setattr(media_routes, "get_storage", lambda: s)
    return s
