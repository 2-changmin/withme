"""사진 전처리·출력 검증 — 윤승희 (#44)

보내기 전: 원본에서 반려견 영역만 잘라 다시 저장한다 (사람·배경 최소화, EXIF·위치 정보 제거).
받은 뒤: 4면 결과가 해상도·흰 배경·다리 분리 조건을 지키는지 검사한다.

강아지 검출은 기기 안에서 MediaPipe(EfficientDet-Lite0, COCO "dog")로 한다.
원본을 외부로 보내지 않기 위해서다 (docs/decisions/2026-10-28-gemini-data-handling.md).
"""
import io
import urllib.request
from functools import lru_cache
from pathlib import Path

from PIL import Image as PILImage
from PIL import ImageOps

from app.errors import AppError, ErrorCode

Image = tuple[bytes, str]
Box = tuple[int, int, int, int]  # x, y, w, h

MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/object_detector/"
             "efficientdet_lite0/float16/latest/efficientdet_lite0.tflite")
MODEL_PATH = Path(__file__).resolve().parents[2] / ".models" / "efficientdet_lite0.tflite"

MIN_SIZE = 1024
PAD = 0.08  # 잘라낼 때 강아지 상자 바깥으로 두는 여유 (털 끝이 잘리지 않게)


@lru_cache
def _detector():
    # 무거운 import라 처음 쓸 때만 불러온다
    from mediapipe.tasks.python import core, vision

    if not MODEL_PATH.exists():
        MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    return vision.ObjectDetector.create_from_options(vision.ObjectDetectorOptions(
        base_options=core.base_options.BaseOptions(model_asset_path=str(MODEL_PATH)),
        score_threshold=0.3, category_allowlist=["dog"],
    ))


def _dog_boxes(img: PILImage.Image) -> list[Box]:
    import mediapipe as mp
    import numpy as np

    frame = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.asarray(img.convert("RGB")))
    return [(d.bounding_box.origin_x, d.bounding_box.origin_y,
             d.bounding_box.width, d.bounding_box.height)
            for d in _detector().detect(frame).detections]


def pad_box(box: Box, width: int, height: int, pad: float = PAD) -> tuple[int, int, int, int]:
    """상자를 pad 비율만큼 넓히고 이미지 안으로 자른다 → (left, top, right, bottom)."""
    x, y, w, h = box
    dx, dy = int(w * pad), int(h * pad)
    return max(0, x - dx), max(0, y - dy), min(width, x + w + dx), min(height, y + h + dy)


def crop_dog(data: bytes, boxes_fn=_dog_boxes) -> Image:
    """원본 사진 → 가장 큰 강아지 영역만 담은 JPEG. EXIF(위치 포함)는 남기지 않는다."""
    try:
        img = ImageOps.exif_transpose(PILImage.open(io.BytesIO(data)))
    except Exception as e:
        raise AppError(ErrorCode.VALIDATION_FAILED, f"사진을 열 수 없음: {e}") from e

    boxes = boxes_fn(img)
    if not boxes:
        raise AppError(ErrorCode.VALIDATION_FAILED, "사진에서 강아지를 찾지 못함")
    # ponytail: 여러 마리면 가장 큰 상자를 고른다. 대상 선택 화면이 생기면 payload로 상자를 받는다.
    box = max(boxes, key=lambda b: b[2] * b[3])

    out = io.BytesIO()
    img.crop(pad_box(box, *img.size)).convert("RGB").save(out, "JPEG", quality=92)
    return out.getvalue(), "image/jpeg"


def _foreground(img: PILImage.Image) -> tuple[list[list[bool]], tuple[int, int, int]]:
    """배경색(테두리 평균)과 다른 픽셀 = 전경. 흰 강아지도 잡히게 기준을 낮게 둔다."""
    small = img.convert("RGB").resize((256, 256))
    px = small.load()
    edge = [px[x, y] for x in range(256) for y in (0, 1, 254, 255)]
    bg = tuple(sum(c[i] for c in edge) // len(edge) for i in range(3))
    mask = [[max(abs(px[x, y][i] - bg[i]) for i in range(3)) > 12 for x in range(256)]
            for y in range(256)]
    return mask, bg


def _runs(row: list[bool], min_len: int = 3, min_gap: int = 3) -> int:
    """한 줄에서 전경 덩어리 수. 짧은 잡음과 좁은 틈은 무시한다."""
    spans: list[list[int]] = []  # [시작, 끝)
    for x, v in enumerate(row):
        if not v:
            continue
        if spans and x - spans[-1][1] < min_gap:
            spans[-1][1] = x + 1  # 좁은 틈은 같은 덩어리로 본다
        else:
            spans.append([x, x + 1])
    return sum(1 for s, e in spans if e - s >= min_len)


def check_view(data: bytes) -> list[str]:
    """4면 결과 1장의 문제 목록. 비어 있으면 통과."""
    img = PILImage.open(io.BytesIO(data))
    problems = []
    if min(img.size) < MIN_SIZE:
        problems.append(f"해상도 부족 {img.size[0]}x{img.size[1]} (최소 {MIN_SIZE})")

    mask, bg = _foreground(img)
    if min(bg) < 225:
        problems.append(f"배경이 흰색이 아님 {bg}")
    # 테두리 8픽셀(배경 그라데이션)은 빼고, 전경이 8픽셀 이상인 줄만 강아지로 본다
    rows = [y for y in range(8, 248) if sum(mask[y][8:248]) >= 8]
    if not rows:
        return [*problems, "강아지가 보이지 않음"]
    # ponytail: 몸 아래쪽 30% 구간에서 다리 덩어리가 2개 이상인 줄이 하나라도 있으면 통과.
    # 바닥 그림자가 발을 이어 붙여도 그 위에서 다리가 갈라져 있으면 된다.
    # 구석 그라데이션 같은 떨어진 줄은 빼고 가장 긴 연속 구간을 강아지 몸으로 본다
    blocks = [[rows[0], rows[0]]]
    for y in rows[1:]:
        if y - blocks[-1][1] <= 3:
            blocks[-1][1] = y
        else:
            blocks.append([y, y])
    top, bottom = max(blocks, key=lambda b: b[1] - b[0])
    h = bottom - top
    if not any(_runs(mask[y]) >= 2 for y in range(bottom - int(h * 0.3), bottom - int(h * 0.03))):
        problems.append("다리가 서로 붙어 보임")
    return problems
