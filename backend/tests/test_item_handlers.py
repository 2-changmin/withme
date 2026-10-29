"""item_cutout·item_views 처리 함수. 누끼 모델과 Gemini는 부르지 않는다."""
import io
import uuid
from types import SimpleNamespace

import pytest
from PIL import Image

from app import media, pets
from app.errors import AppError, ErrorCode
from app.worker import photo_prep
from app.worker.handlers import JobContext, item_cutout, item_views

pytestmark = pytest.mark.db


@pytest.fixture
def pet(storage, monkeypatch):
    monkeypatch.setattr(item_views.gemini_views, "get_settings",
                        lambda: SimpleNamespace(mock_gemini=True))
    user_id = uuid.uuid4()
    return user_id, pets.create(user_id, "콩이")["id"]


def run(handler, pet, payload):
    user_id, pet_id = pet
    job = {"user_id": user_id, "payload": {"petId": str(pet_id), "itemId": "i1", **payload}}
    return handler.handle(JobContext(job, lambda _: None))


def _photo() -> bytes:
    buf = io.BytesIO()
    Image.new("RGB", (40, 40), "blue").save(buf, "JPEG")
    return buf.getvalue()


def test_cutout_saves_png_from_source(pet, monkeypatch):
    monkeypatch.setattr(photo_prep, "_remove_bg", lambda img: img.convert("RGBA"))
    src = media.save_generated(*pet, _photo(), "image/jpeg", source_type="upload")

    result = run(item_cutout, pet, {"sourceMediaId": src})

    row = media.get(result["cutoutMediaId"])
    assert result["itemId"] == "i1"
    assert row["mime_type"] == "image/png" and str(row["parent_id"]) == src


def test_views_save_four_from_cutout(pet):
    cut = media.save_generated(*pet, b"png", "image/png", source_type="generated_visual")

    result = run(item_views, pet, {"kind": "toy", "cutoutMediaId": cut})

    assert len(result["viewMediaIds"]) == 4
    assert all(media.load(m, pet[1])[0] == b"png" for m in result["viewMediaIds"])


def test_views_reject_unknown_kind(pet):
    with pytest.raises(AppError) as e:
        run(item_views, pet, {"kind": "car", "cutoutMediaId": "x"})
    assert e.value.code == ErrorCode.VALIDATION_FAILED
