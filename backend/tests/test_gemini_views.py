"""4면 생성 순서 테스트. Gemini를 호출하지 않는 가짜 클라이언트로 돌린다."""
from types import SimpleNamespace

import pytest
from google.genai import errors

from app.errors import AppError, ErrorCode
from app.worker import gemini_views


class FakeModels:
    def __init__(self, fail_with=None):
        self.calls = []
        self.fail_with = fail_with

    def generate_content(self, model, contents, config):
        if self.fail_with:
            raise self.fail_with
        self.calls.append(contents)
        img = SimpleNamespace(data=f"img{len(self.calls)}".encode(), mime_type="image/png")
        part = SimpleNamespace(inline_data=img)
        return SimpleNamespace(candidates=[SimpleNamespace(content=SimpleNamespace(parts=[part]))])


def fake_client(**kw):
    return SimpleNamespace(models=FakeModels(**kw))


# 검출 모델·이미지 검사 없이 순서만 본다
NO_PREP = {"prep": lambda d: (d, "image/jpeg"), "check": lambda d: []}


def test_side_first_then_three_views_reference_side():
    client = fake_client()
    sources = [(b"a", "image/jpeg"), (b"b", "image/jpeg")]
    seen = []

    out, problems = gemini_views.generate_views(sources, seen.append, client=client, **NO_PREP)

    assert list(out) == list(gemini_views.VIEWS)
    calls = client.models.calls
    assert len(calls) == 4
    assert len(calls[0]) == 3                      # 원본 2장 + 프롬프트
    for c in calls[1:]:
        assert len(c) == 4                         # 원본 2장 + 기준 옆모습 + 프롬프트
        assert c[2].inline_data.data == b"img1"    # 기준 옆모습을 참조로 넣음
    assert seen == [25, 50, 75, 100]
    assert problems == {}


def test_failed_check_regenerates_once_then_reports():
    client = fake_client()
    # side는 첫 결과만 실패, front는 두 번 다 실패
    bad = {b"img1", b"img3", b"img4"}
    check = lambda d: ["다리가 서로 붙어 보임"] if d in bad else []

    out, problems = gemini_views.generate_views(
        [(b"a", "image/jpeg")], client=client, prep=NO_PREP["prep"], check=check)

    assert len(client.models.calls) == 6          # 4면 + side 재시도 1 + front 재시도 1
    assert out["side"][0] == b"img2"              # 재시도 결과를 기준으로 씀
    assert problems == {"front": ["다리가 서로 붙어 보임"]}


def test_rejects_source_count():
    with pytest.raises(AppError) as e:
        gemini_views.generate_views([], client=fake_client())
    assert e.value.code == ErrorCode.VALIDATION_FAILED


@pytest.mark.parametrize("status,code", [(429, ErrorCode.UPSTREAM_FAILED),
                                         (503, ErrorCode.UPSTREAM_FAILED),
                                         (400, ErrorCode.VALIDATION_FAILED)])
def test_api_errors_map_to_retry_or_not(status, code):
    err = errors.APIError(status, {"error": {"message": "x", "status": "X"}})
    with pytest.raises(AppError) as e:
        gemini_views.generate_views([(b"a", "image/jpeg")], client=fake_client(fail_with=err),
                                    **NO_PREP)
    assert e.value.code == code


def test_mock_mode_does_not_call_gemini(monkeypatch):
    monkeypatch.setattr(gemini_views, "get_settings",
                        lambda: SimpleNamespace(mock_gemini=True))
    src = (b"photo", "image/jpeg")
    out, problems = gemini_views.generate_views([src])
    assert out == dict.fromkeys(gemini_views.VIEWS, src)
    assert problems == {}
