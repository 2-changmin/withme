"""무지개다리 편지 답장 쓰기 (Gemini 텍스트) — 박재현 (#74)

docs/decisions/2026-10-30-rainbow-letter.md의 안전장치를 프롬프트와 결과 검사로 지킨다.
- 위기 표현이면 답장 대신 crisis=true (API 키워드 검사를 빠져나간 경우)
- 금지 표현(재회 약속, 탓하기, 요구)이 나오면 한 번 다시 쓰고, 또 나오면 정해진 답장을 쓴다

로컬 테스트 (MOCK_LETTER=false, GEMINI_API_KEY 필요):
  cd backend && .venv/bin/python -m app.worker.letter_writer 콩이 "콩아 보고 싶어"
"""
import json
import logging
import sys

from google import genai
from google.genai import errors, types

from app.config import get_settings
from app.errors import AppError, ErrorCode

log = logging.getLogger(__name__)

SYSTEM = """\
너는 세상을 떠난 반려견 '{name}'의 말투로, 보호자가 보낸 편지에 짧은 답장을 쓴다.
답장은 AI가 쓴 것이라고 화면에 함께 표시된다.

지킬 것
- 반말, 반려견 1인칭. 3~6문장, 300자 이내. 따뜻하고 차분하게.
- 편지에 나온 기억과 마음에만 답한다. 편지에 없는 사건·장소·사람을 지어내지 않는다.
- 보호자의 슬픔과 사랑을 그대로 받아 주고, 고마움을 전한다.

하지 않을 것
- 살아 있다·돌아온다·다시 만나자·기다리겠다는 말, 사후 세계를 단정하는 말
- 보호자를 탓하거나 죄책감에 동의하는 말 ("그때 네가 ~했더라면")
- 무언가를 하라고 요구하는 말 (매일 편지 쓰기, 앱 사용, 결제)
- 진단·의학적 판단

편지에 자해·자살, 따라가고 싶다는 마음, 삶을 끝내고 싶다는 표현이 있으면
답장을 쓰지 말고 crisis를 true로 한다.
"""

_SCHEMA = types.Schema(
    type=types.Type.OBJECT,
    properties={
        "crisis": types.Schema(type=types.Type.BOOLEAN),
        "reply": types.Schema(type=types.Type.STRING),
    },
    required=["crisis", "reply"],
)

# 답장에서 나오면 안 되는 표현 (공백을 지우고 찾는다). 재회 약속·기다림·요구.
# 탓하기는 "네 탓이 아니야"처럼 위로하는 말과 구분이 어려워 프롬프트로만 막는다.
BANNED = (
    "다시만나", "다시만날", "기다릴게", "기다리고있을", "돌아갈게", "돌아올게",
    "하늘나라에서기다", "매일편지", "매일써",
)


def _has_batchim(word: str) -> bool:
    last = word[-1] if word else ""
    return "가" <= last <= "힣" and (ord(last) - 0xAC00) % 28 != 0


def fallback_reply(name: str) -> str:
    """Gemini를 쓰지 않을 때·금지 표현이 두 번 나왔을 때 쓰는 정해진 답장."""
    me = f"{name}{'이야' if _has_batchim(name) else '야'}"
    return (f"나 {me}. 편지 고마워. 네가 나를 떠올려 준 마음이 다 느껴졌어. "
            "우리가 함께한 시간은 나한테도 정말 따뜻하고 행복했어. "
            "오늘은 너무 애쓰지 말고, 맛있는 거 먹고 푹 쉬어. 언제나 고마워.")


def is_safe(reply: str) -> bool:
    compact = "".join(reply.split())
    return bool(compact) and not any(b in compact for b in BANNED)


def _ask(client: genai.Client, name: str, body: str) -> dict:
    try:
        res = client.models.generate_content(
            model=get_settings().gemini_text_model,
            contents=f"보호자의 편지:\n{body}",
            config=types.GenerateContentConfig(
                system_instruction=SYSTEM.format(name=name),
                response_mime_type="application/json",
                response_schema=_SCHEMA,
                temperature=0.7,
            ),
        )
    except errors.APIError as e:
        # 429·5xx는 일시 장애 → 작업자가 재시도. 나머지(잘못된 요청·키)는 재시도해도 같다.
        code = ErrorCode.UPSTREAM_FAILED if e.code == 429 or e.code >= 500 else ErrorCode.VALIDATION_FAILED
        raise AppError(code, f"Gemini {e.code}: {e.message}") from e
    try:
        out = json.loads(res.text or "")
        return {"crisis": bool(out["crisis"]), "reply": str(out.get("reply") or "").strip()}
    except (ValueError, KeyError, TypeError) as e:
        raise AppError(ErrorCode.UPSTREAM_FAILED, f"Gemini 응답 형식 오류: {res.text!r:.200}") from e


def write_reply(name: str, body: str) -> dict:
    """결과: {"crisis": bool, "reply": str | None}"""
    s = get_settings()
    if s.mock_letter:
        return {"crisis": False, "reply": fallback_reply(name)}
    if not s.gemini_api_key:
        raise AppError(ErrorCode.INTERNAL, "GEMINI_API_KEY가 없습니다 (backend/.env)")
    client = genai.Client(api_key=s.gemini_api_key)
    for _ in range(2):
        out = _ask(client, name, body)
        if out["crisis"]:
            return {"crisis": True, "reply": None}
        if is_safe(out["reply"]):
            return out
        log.warning("답장에 금지 표현이 있어 다시 씁니다")
    return {"crisis": False, "reply": fallback_reply(name)}


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    print(write_reply(sys.argv[1], sys.argv[2]))
