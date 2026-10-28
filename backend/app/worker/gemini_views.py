"""4면 이미지 생성 (Gemini) — 윤승희 (#38, #39)

원본 사진 여러 장 → 서 있는 옆모습 기준 1장 → 기준 + 원본으로 정면·후면·반대쪽 옆모습.
4장을 따로 만들면 무늬·귀 모양이 달라지므로 기준 1장을 먼저 만든다 (docs/03-3d-pipeline.md 1장).

로컬 테스트 (팀원 반려견·공개 사진만 사용, #41 확인 전 실제 보호자 사진 금지):
  cd backend && .venv/bin/python -m app.worker.gemini_views dog1.jpg dog2.jpg -o out/
"""
import argparse
import mimetypes
from collections.abc import Callable
from pathlib import Path

from google import genai
from google.genai import errors, types

from app.config import get_settings
from app.errors import AppError, ErrorCode

Image = tuple[bytes, str]  # (데이터, MIME)

# 결과 순서 = reference_views.view_media_ids 순서
VIEWS = ("side", "front", "back", "other")

_STYLE = (
    "Soft low-poly toy style. Plain white background, even lighting, no shadow, "
    "no text, no clothes or accessories, full body visible and centered."
)
# 자동 리깅 조건: 네 다리가 서로 떨어짐, 꼬리가 몸에서 떨어짐, 입 닫힘
_POSE = (
    "Standing on all four legs, legs straight and slightly apart so no two legs touch, "
    "tail extended away from the body, mouth closed, neutral pose."
)
SIDE_PROMPT = (
    "Same dog as in the reference photos. Keep exact fur color, markings, ear shape, and face. "
    f"Full body side view, facing left. {_POSE} {_STYLE}"
)
_VIEW_TEXT = {
    "front": "front view, facing the camera",
    "back": "back view, seen from behind",
    "other": "full body side view, facing right (mirror direction of the last image)",
}


def view_prompt(view: str) -> str:
    return (
        "The last image is the reference: keep exactly the same dog, same fur color and markings, "
        f"same ear shape, same proportions, same pose and style. Draw its {_VIEW_TEXT[view]}. "
        f"{_POSE} {_STYLE}"
    )


def _client() -> genai.Client:
    key = get_settings().gemini_api_key
    if not key:
        raise AppError(ErrorCode.INTERNAL, "GEMINI_API_KEY가 없습니다 (backend/.env)")
    return genai.Client(api_key=key)


def _generate(client, refs: list[Image], prompt: str) -> Image:
    parts = [types.Part.from_bytes(data=d, mime_type=m) for d, m in refs]
    try:
        res = client.models.generate_content(
            model=get_settings().gemini_image_model,
            contents=[*parts, prompt],
            config=types.GenerateContentConfig(
                response_modalities=["IMAGE"],
                image_config=types.ImageConfig(aspect_ratio="1:1"),
            ),
        )
    except errors.APIError as e:
        # 429·5xx는 일시 장애 → 작업자가 재시도. 나머지(잘못된 요청·키)는 재시도해도 같다.
        code = ErrorCode.UPSTREAM_FAILED if e.code == 429 or e.code >= 500 else ErrorCode.VALIDATION_FAILED
        raise AppError(code, f"Gemini {e.code}: {e.message}") from e

    for cand in res.candidates or []:
        for part in (cand.content.parts if cand.content else None) or []:
            if part.inline_data and part.inline_data.data:
                return part.inline_data.data, part.inline_data.mime_type or "image/png"
    # 안전 필터 등으로 이미지 없이 끝난 경우. 같은 입력이면 다시 막힐 가능성이 높다.
    reason = res.candidates[0].finish_reason if res.candidates else res.prompt_feedback
    raise AppError(ErrorCode.UPSTREAM_FAILED, f"Gemini가 이미지를 돌려주지 않음: {reason}")


def generate_views(sources: list[Image], progress: Callable[[int], None] = lambda _: None,
                   client=None) -> dict[str, Image]:
    """원본 1~4장 → {"side", "front", "back", "other"} 이미지."""
    if not 1 <= len(sources) <= 4:
        raise AppError(ErrorCode.VALIDATION_FAILED, "원본 사진은 1~4장입니다")
    client = client or _client()

    side = _generate(client, sources, SIDE_PROMPT)
    out = {"side": side}
    progress(25)
    for i, view in enumerate(VIEWS[1:], start=2):
        out[view] = _generate(client, [*sources, side], view_prompt(view))
        progress(25 * i)
    return out


def _main() -> None:
    p = argparse.ArgumentParser(description="원본 사진 → 4면 이미지 (Gemini)")
    p.add_argument("photos", nargs="+", type=Path, help="원본 사진 1~4장")
    p.add_argument("-o", "--out", type=Path, default=Path("out"))
    a = p.parse_args()

    sources = [(f.read_bytes(), mimetypes.guess_type(f)[0] or "image/jpeg") for f in a.photos]
    a.out.mkdir(parents=True, exist_ok=True)
    print(f"모델: {get_settings().gemini_image_model}")
    for view, (data, mime) in generate_views(sources, lambda n: print(f"  {n}%")).items():
        path = a.out / f"{view}{mimetypes.guess_extension(mime) or '.png'}"
        path.write_bytes(data)
        print(f"저장: {path}")


if __name__ == "__main__":
    _main()
