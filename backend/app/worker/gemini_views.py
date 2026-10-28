"""4면 이미지 생성 (Gemini) — 윤승희 (#38, #39)

원본 사진 여러 장 → 서 있는 옆모습 기준 1장 → 기준 + 원본으로 정면·후면·반대쪽 옆모습.
4장을 따로 만들면 무늬·귀 모양이 달라지므로 기준 1장을 먼저 만든다 (docs/03-3d-pipeline.md 1장).

로컬 테스트 (팀원 반려견·공개 사진만 사용, #41 확인 전 실제 보호자 사진 금지):
  cd backend && .venv/bin/python -m app.worker.gemini_views dog1.jpg dog2.jpg -o out/
"""
import argparse
import logging
import mimetypes
from collections.abc import Callable
from pathlib import Path

from google import genai
from google.genai import errors, types

from app.config import get_settings
from app.errors import AppError, ErrorCode
from app.worker import photo_prep

log = logging.getLogger(__name__)

Image = tuple[bytes, str]  # (데이터, MIME)

# 결과 순서 = reference_views.view_media_ids 순서
VIEWS = ("side", "front", "back", "other")

# 실사 사진 느낌 유지. 털이 다리를 덮으면 리깅이 깨지므로 다리 윤곽은 보이게 한다.
_STYLE = (
    "Photorealistic studio photograph of the real dog, natural fur texture and true colors, "
    "same coat length and grooming as in the photos, but each leg's outline clearly visible. "
    "Plain white background, soft even studio lighting, no shadow, no text, "
    "no clothes or accessories, camera at the dog's height. "
    "The whole dog is centered and fills about 80% of the image width."
)
# 자동 리깅 조건: 네 다리가 서로 떨어짐, 꼬리가 몸에서 떨어짐, 입 닫힘
_POSE = (
    "Standing on all four legs, legs straight and slightly apart so no two legs touch, "
    "Tail raised up and held out behind the body as the dog would when alert and happy, "
    "with a clear gap of white background between the tail and the hind legs, rump and back; "
    "the tail must not hang down or touch any leg. Mouth closed, neutral pose."
)
SIDE_PROMPT = (
    "Same dog as in the reference photos. Keep exact fur color, markings, ear shape, and face. "
    "Strict side profile: head on the LEFT side of the image, tail on the RIGHT side. "
    "Head in line with the body, looking straight ahead toward the left edge, not at the camera. "
    f"{_POSE} {_STYLE}"
)
_VIEW_TEXT = {
    "front": "straight front view, facing the camera, body symmetric",
    "back": ("straight back view seen from directly behind: show the rump, tail and back of the head, "
             "the face and eyes must NOT be visible"),
    "other": ("strict side profile of the dog's other side: head on the RIGHT side of the image, "
              "tail on the LEFT side, looking straight ahead toward the right edge, not at the camera"),
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
                   client=None, prep=photo_prep.crop_dog, check=photo_prep.check_view,
                   ) -> tuple[dict[str, Image], dict[str, list[str]]]:
    """원본 1~4장 → ({"side", "front", "back", "other"} 이미지, 면별 남은 문제).

    원본은 반려견 영역만 잘라서 보낸다. 결과가 검사(해상도·흰 배경·다리 분리)에
    걸리면 그 면만 1번 다시 만들고, 그래도 남은 문제는 보호자 확인 화면에 넘긴다.
    """
    if not 1 <= len(sources) <= 4:
        raise AppError(ErrorCode.VALIDATION_FAILED, "원본 사진은 1~4장입니다")
    if client is None and get_settings().mock_gemini:
        log.warning("MOCK_GEMINI=true: Gemini를 부르지 않고 원본 사진을 4면 결과로 돌려줍니다")
        progress(100)
        return dict.fromkeys(VIEWS, sources[0]), {}
    client = client or _client()
    sources = [prep(data) for data, _ in sources]

    out: dict[str, Image] = {}
    problems: dict[str, list[str]] = {}
    for i, view in enumerate(VIEWS, start=1):
        refs, prompt = (sources, SIDE_PROMPT) if view == "side" else (
            [*sources, out["side"]], view_prompt(view))
        for _ in range(2):  # 검사 실패 시 1번 다시
            out[view] = _generate(client, refs, prompt)
            if not (found := check(out[view][0])):
                problems.pop(view, None)
                break
            log.warning("%s 검사 실패: %s", view, found)
            problems[view] = found
        progress(25 * i)
    return out, problems


def _main() -> None:
    p = argparse.ArgumentParser(description="원본 사진 → 4면 이미지 (Gemini)")
    p.add_argument("photos", nargs="+", type=Path, help="원본 사진 1~4장")
    p.add_argument("-o", "--out", type=Path, default=Path("out"))
    a = p.parse_args()

    sources = [(f.read_bytes(), mimetypes.guess_type(f)[0] or "image/jpeg") for f in a.photos]
    a.out.mkdir(parents=True, exist_ok=True)
    s = get_settings()
    print("모의 모드 (MOCK_GEMINI=true)" if s.mock_gemini else f"모델: {s.gemini_image_model}")
    views, problems = generate_views(sources, lambda n: print(f"  {n}%"))
    for view, (data, mime) in views.items():
        path = a.out / f"{view}{mimetypes.guess_extension(mime) or '.png'}"
        path.write_bytes(data)
        print(f"저장: {path}", f"⚠️ {problems[view]}" if view in problems else "")


if __name__ == "__main__":
    _main()
