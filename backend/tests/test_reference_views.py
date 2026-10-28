"""reference_views 작업 처리 함수. MOCK_GEMINI 상태로 돌려 Gemini를 부르지 않는다."""
from types import SimpleNamespace

import pytest

from app.errors import AppError, ErrorCode
from app.worker.handlers import JobContext, reference_views


@pytest.fixture
def media(tmp_path, monkeypatch):
    settings = SimpleNamespace(local_media_dir=str(tmp_path), mock_gemini=True)
    monkeypatch.setattr(reference_views, "get_settings", lambda: settings)
    monkeypatch.setattr(reference_views.gemini_views, "get_settings", lambda: settings)
    return tmp_path


def run(payload):
    seen = []
    return reference_views.handle(JobContext({"payload": payload}, seen.append)), seen


def test_saves_four_views_and_reports(media):
    src = reference_views.save_media(b"photo", "image/jpeg")

    result, seen = run({"petId": "p1", "referenceViewId": "rv1", "sourceMediaIds": [src]})

    assert result["referenceViewId"] == "rv1"
    assert len(result["viewMediaIds"]) == 4
    assert all(reference_views.load_media(m)[0] == b"photo" for m in result["viewMediaIds"])
    assert result["problems"] == {}
    assert seen[0] == 5 and seen[-1] == 95


@pytest.mark.parametrize("payload,code", [
    ({"sourceMediaIds": []}, ErrorCode.VALIDATION_FAILED),
    ({"sourceMediaIds": ["missing"]}, ErrorCode.VALIDATION_FAILED),
    ({"sourceMediaIds": ["x"], "attempt": 4}, ErrorCode.LIMIT_EXCEEDED),
])
def test_rejects_bad_payload(media, payload, code):
    with pytest.raises(AppError) as e:
        run(payload)
    assert e.value.code == code
