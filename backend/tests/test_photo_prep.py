"""사진 전처리·출력 검사 테스트. 검출 모델 없이 합성 이미지로 돈다."""
import io

import pytest
from PIL import Image, ImageDraw

from app.errors import AppError, ErrorCode
from app.worker import photo_prep


def png(size=1024, legs=((300, 420), (600, 720))) -> bytes:
    """흰 배경에 몸통 + 다리 사각형. legs = 다리별 (x 시작, x 끝)."""
    img = Image.new("RGB", (size, size), "white")
    d = ImageDraw.Draw(img)
    s = size / 1024
    d.rectangle([250 * s, 300 * s, 780 * s, 600 * s], fill=(200, 160, 110))
    for x0, x1 in legs:
        d.rectangle([x0 * s, 600 * s, x1 * s, 850 * s], fill=(200, 160, 110))
    out = io.BytesIO()
    img.save(out, "PNG")
    return out.getvalue()


def test_separate_legs_pass():
    assert photo_prep.check_view(png()) == []


def test_merged_legs_fail():
    assert "다리가 서로 붙어 보임" in photo_prep.check_view(png(legs=((300, 720),)))


def test_small_image_fails():
    assert any("해상도" in p for p in photo_prep.check_view(png(size=512)))


def test_pad_box_clamps_to_image():
    assert photo_prep.pad_box((10, 10, 100, 100), 105, 200, pad=0.1) == (0, 0, 105, 120)


def test_crop_takes_largest_dog_and_drops_exif():
    src = Image.new("RGB", (400, 300), "gray")
    exif = Image.Exif()
    exif[0x8825] = {2: (37.0, 30.0, 0.0)}  # GPS
    buf = io.BytesIO()
    src.save(buf, "JPEG", exif=exif)

    data, mime = photo_prep.crop_dog(
        buf.getvalue(), boxes_fn=lambda _: [(0, 0, 50, 50), (100, 100, 200, 100)])
    out = Image.open(io.BytesIO(data))
    assert mime == "image/jpeg"
    assert out.size == (232, 116)  # 큰 상자 + 여유 8%
    assert not out.getexif()


def test_crop_without_dog_is_validation_error():
    buf = io.BytesIO()
    Image.new("RGB", (10, 10)).save(buf, "PNG")
    with pytest.raises(AppError) as e:
        photo_prep.crop_dog(buf.getvalue(), boxes_fn=lambda _: [])
    assert e.value.code == ErrorCode.VALIDATION_FAILED
