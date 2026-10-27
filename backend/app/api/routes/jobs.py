from uuid import UUID

from fastapi import APIRouter

from app import queue
from app.api.routes.character import MOCK_PREFIX, mock_job
from app.auth import CurrentUser
from app.errors import AppError, ErrorCode
from app.schemas import Job

router = APIRouter(tags=["jobs"])


@router.get("/jobs/{job_id}", response_model=Job)
def get_job(job_id: str, user_id: CurrentUser) -> Job:
    """작업 상태·진행률·결과. 앱은 3초마다 조회한다."""
    if job_id.startswith(MOCK_PREFIX):
        return mock_job(job_id)
    try:
        uid = UUID(job_id)
    except ValueError as e:
        raise AppError(ErrorCode.NOT_FOUND, "작업이 없습니다") from e
    row = queue.get_job(uid, user_id)
    if row is None:
        raise AppError(ErrorCode.NOT_FOUND, "작업이 없습니다")
    return Job.model_validate(row)
