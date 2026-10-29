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


# ---- 물건 누끼 (#76) ----

def _jpeg(w, h) -> bytes:
    buf = io.BytesIO()
    exif = Image.Exif()
    exif[0x0110] = "phone"  # 카메라 모델
    Image.new("RGB", (w, h), "gray").save(buf, "JPEG", exif=exif)
    return buf.getvalue()


def _fake_remove(img):
    """가운데 20x10만 남기고 나머지는 투명."""
    out = img.convert("RGBA")
    out.putalpha(0)
    out.paste((255, 0, 0, 255), (40, 45, 60, 55))
    return out


def test_cutout_crops_to_object_as_png():
    data, mime = photo_prep.cutout(_jpeg(100, 100), remove_fn=_fake_remove)
    img = Image.open(io.BytesIO(data))
    assert mime == "image/png" and img.mode == "RGBA"
    assert img.size == (20 + 2 * int(20 * photo_prep.PAD), 10 + 2 * int(10 * photo_prep.PAD))
    assert not img.info.get("exif")


def test_cutout_without_object_fails():
    def nothing(img):
        out = img.convert("RGBA")
        out.putalpha(0)
        return out

    with pytest.raises(AppError) as e:
        photo_prep.cutout(_jpeg(50, 50), remove_fn=nothing)
    assert e.value.code == ErrorCode.VALIDATION_FAILED


def test_on_white_flattens_transparency():
    buf = io.BytesIO()
    Image.new("RGBA", (4, 4), (0, 0, 0, 0)).save(buf, "PNG")
    data, mime = photo_prep.on_white(buf.getvalue())
    assert mime == "image/jpeg"
    assert Image.open(io.BytesIO(data)).getpixel((1, 1)) == (255, 255, 255)
