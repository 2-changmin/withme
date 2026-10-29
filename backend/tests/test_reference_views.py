"""reference_views 작업 처리 함수. MOCK_GEMINI 상태로 돌려 Gemini를 부르지 않는다."""
import uuid
from types import SimpleNamespace

import pytest

from app import media, pets
from app.errors import AppError, ErrorCode
from app.schemas import ConsentItem
from app.worker.handlers import JobContext, reference_views

pytestmark = pytest.mark.db


@pytest.fixture
def pet(storage, monkeypatch):
    settings = SimpleNamespace(mock_gemini=True)
    monkeypatch.setattr(reference_views.gemini_views, "get_settings", lambda: settings)
    user_id = uuid.uuid4()
    pet_id = pets.create(user_id, "콩이")["id"]
    pets.add_consents(user_id, pet_id, [ConsentItem.GEMINI_TRANSFER], "test")
    return user_id, pet_id


def run(pet, payload):
    user_id, pet_id = pet
    seen = []
    job = {"user_id": user_id, "payload": {"petId": str(pet_id), **payload}}
    return reference_views.handle(JobContext(job, seen.append)), seen


def test_saves_four_views_and_reports(pet):
    src = media.save_generated(*pet, b"photo", "image/jpeg", source_type="upload")

    result, seen = run(pet, {"referenceViewId": "rv1", "sourceMediaIds": [src]})

    assert result["referenceViewId"] == "rv1"
    assert len(result["viewMediaIds"]) == 4
    for m in result["viewMediaIds"]:
        assert media.load(m, pet[1])[0] == b"photo"
        row = media.get(m)
        assert row["source_type"] == "generated_visual" and str(row["parent_id"]) == src
    assert result["problems"] == {}
    assert seen[0] == 5 and seen[-1] == 95


def test_rejects_other_pets_photo(pet):
    other = uuid.uuid4()
    src = media.save_generated(other, pets.create(other, "두부")["id"], b"photo", "image/jpeg",
                               source_type="upload")
    with pytest.raises(AppError) as e:
        run(pet, {"sourceMediaIds": [src]})
    assert e.value.code == ErrorCode.VALIDATION_FAILED


@pytest.mark.parametrize("payload,code", [
    ({"sourceMediaIds": []}, ErrorCode.VALIDATION_FAILED),
    ({"sourceMediaIds": ["missing"]}, ErrorCode.VALIDATION_FAILED),
    ({"sourceMediaIds": [str(uuid.uuid4())]}, ErrorCode.VALIDATION_FAILED),
    ({"sourceMediaIds": ["x"], "attempt": 4}, ErrorCode.LIMIT_EXCEEDED),
])
def test_rejects_bad_payload(pet, payload, code):
    with pytest.raises(AppError) as e:
        run(pet, payload)
    assert e.value.code == code


def test_refuses_without_gemini_consent(storage, monkeypatch):
    user_id = uuid.uuid4()
    pet_id = pets.create(user_id, "두부")["id"]
    pets.add_consents(user_id, pet_id, [ConsentItem.MESHY_TRANSFER], "test")  # Meshy만 동의
    src = media.save_generated(user_id, pet_id, b"photo", "image/jpeg", source_type="upload")
    called = []
    monkeypatch.setattr(reference_views.gemini_views, "generate_views",
                        lambda *a, **k: called.append(1))
    with pytest.raises(AppError) as e:
        run((user_id, pet_id), {"sourceMediaIds": [src]})
    assert e.value.code == ErrorCode.FORBIDDEN
    assert called == []  # Gemini 단계까지 가지 않음
