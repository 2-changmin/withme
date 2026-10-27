"""소리 분할 — 박재현 (#33)

payload: {"mediaId"}
result:  {"mediaId", "clipIds": [...]}

영상이면 오디오 추출 → ffmpeg silencedetect로 구간 나누기 → 조각 저장 (원본 유지)
"""
from typing import Any

from app.worker.handlers import JobContext


def handle(ctx: JobContext) -> dict[str, Any]:
    raise NotImplementedError("sound_split: 박재현 구현 예정 (#33)")
