from uuid import UUID

from fastapi import APIRouter

from app import media, queue
from app.api.routes.character import MOCK_PREFIX, mock_job
from app.auth import CurrentUser
from app.errors import AppError, ErrorCode
from app.schemas import Job

router = APIRouter(tags=["jobs"])

# 작업 결과의 media ID → 앱에 내려줄 서명 URL 키 (#23). ID도 그대로 둔다.
# URL은 SIGNED_URL_SECONDS 뒤에 만료되므로 앱은 화면을 다시 열 때 이 API를 다시 부른다.
_URL_KEYS = {
    "viewMediaIds": "views",
    "rawGlbMediaId": "glbUrl",
    "preparedGlbMediaId": "preparedGlbUrl",
    "riggedGlbMediaId": "riggedGlbUrl",
}


def _url(media_id: str) -> str | None:
    try:
        return media.download_url(media.get(media_id))
    except AppError:
        return None


def _with_urls(result: dict | None) -> dict | None:
    if not result:
        return result
    out = dict(result)
    for id_key, url_key in _URL_KEYS.items():
        value = result.get(id_key)
        if isinstance(value, list):
            out[url_key] = [_url(v) for v in value]
        elif value:
            out[url_key] = _url(value)
    return out


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
    return Job.model_validate({**row, "result": _with_urls(row["result"])})
