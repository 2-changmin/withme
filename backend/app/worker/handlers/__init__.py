"""작업 종류 → 처리 함수. 각 담당자는 자기 파일의 함수만 구현한다.

처리 함수 규칙
- 입력: JobContext (ctx.payload, ctx.progress(0~100))
- 출력: 결과 dict (jobs.result에 저장되어 앱이 GET /jobs/:id로 받는다)
- 외부 API 일시 장애: AppError(ErrorCode.UPSTREAM_FAILED) → 자동 재시도
- 입력이 잘못됨: AppError(ErrorCode.VALIDATION_FAILED) → 재시도 없이 실패
- 파일 읽기·저장: app.media의 load / save_generated / signed_url
"""
from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

from app.schemas import JobType


@dataclass
class JobContext:
    job: dict[str, Any]
    progress: Callable[[int], None]

    @property
    def payload(self) -> dict[str, Any]:
        return self.job["payload"]


Handler = Callable[[JobContext], dict[str, Any]]


def echo(ctx: JobContext) -> dict[str, Any]:
    """파이프라인 점검용: 받은 payload를 그대로 돌려준다."""
    ctx.progress(50)
    return {"echo": ctx.payload}


# 순환 import를 피하려고 아래에서 가져온다
from app.worker.handlers import (
    item_cutout,
    item_views,
    letter_reply,
    media_check,
    mesh_prepare,
    meshy_generate,
    reference_views,
    rig,
    sound_split,
)

HANDLERS: dict[str, Handler] = {
    JobType.ECHO: echo,
    JobType.MEDIA_CHECK: media_check.handle,
    JobType.REFERENCE_VIEWS: reference_views.handle,
    JobType.MESHY_GENERATE: meshy_generate.handle,
    JobType.MESH_PREPARE: mesh_prepare.handle,
    JobType.RIG: rig.handle,
    JobType.SOUND_SPLIT: sound_split.handle,
    JobType.ITEM_CUTOUT: item_cutout.handle,
    JobType.ITEM_VIEWS: item_views.handle,
    JobType.LETTER_REPLY: letter_reply.handle,
}
