"""미디어 업로드·조회 API (docs/04-api-data.md, F03)."""
import mimetypes

from fastapi import APIRouter, Request, Response

from app import media, queue
from app.auth import CurrentUser
from app.config import get_settings
from app.errors import AppError, ErrorCode
from app.ownership import OwnedMedia, OwnedPet
from app.schemas import JobAccepted, JobType, Media, UploadCreate, UploadTicket
from app.storage import LocalStorage, get_storage

router = APIRouter(tags=["media"])


def _media(row: dict) -> Media:
    return Media.model_validate({**row, "url": media.download_url(row)})


@router.post("/pets/{pet_id}/uploads", response_model=UploadTicket, status_code=201)
def create_upload(pet_id: OwnedPet, body: UploadCreate, user_id: CurrentUser) -> UploadTicket:
    """업로드 URL 발급. 형식·크기·반려견별 개수와 남은 용량을 먼저 확인한다.

    상한: 사진 10MB·10장, 영상 50MB·3개, 소리 20MB·5개, 전체 200MB (app/media.py KINDS)
    """
    row, url = media.create_upload(user_id, pet_id, body.mime_type, body.size_bytes)
    return UploadTicket(media_id=row["id"], upload_url=url,
                        headers={"Content-Type": row["mime_type"]})


@router.post("/media/{media_id}/finalize", response_model=JobAccepted, status_code=202)
def finalize_upload(media_id: OwnedMedia, user_id: CurrentUser) -> JobAccepted:
    """업로드 완료 알림. 실제 크기를 다시 확인하고 검사 작업(media_check)을 등록한다."""
    row = media.finalize_upload(media_id)
    job_id = queue.enqueue(JobType.MEDIA_CHECK, user_id, {"mediaId": str(media_id)},
                           pet_id=row["pet_id"], idempotency_key=f"media_check:{media_id}")
    return JobAccepted(job_id=job_id)


@router.get("/media/{media_id}", response_model=Media)
def get_media(media_id: OwnedMedia) -> Media:
    """파일 상태와 짧은 만료 서명 URL. URL이 만료되면 다시 조회한다."""
    return _media(media.get(media_id))


# ---- STORAGE_BACKEND=local 전용: 서명 URL이 가리키는 곳 ----

def _local(op: str, path: str, exp: int, sig: str) -> LocalStorage:
    if get_settings().storage_backend != "local":
        raise AppError(ErrorCode.NOT_FOUND, "없는 경로")
    storage = get_storage()
    if not storage.verify(op, path, exp, sig):
        raise AppError(ErrorCode.FORBIDDEN, "서명이 틀렸거나 만료됐습니다")
    return storage


@router.put("/storage/local/{path:path}", status_code=200, include_in_schema=False)
async def local_put(path: str, exp: int, sig: str, request: Request) -> dict:
    storage = _local("put", path, exp, sig)
    limit = max(k.max_bytes for k in media.KINDS.values())
    if int(request.headers.get("content-length", 0)) > limit:
        raise AppError(ErrorCode.LIMIT_EXCEEDED, "파일이 너무 큽니다")
    storage.put(path, await request.body(), request.headers.get("content-type", ""))
    return {}


@router.get("/storage/local/{path:path}", include_in_schema=False)
def local_get(path: str, exp: int, sig: str) -> Response:
    storage = _local("get", path, exp, sig)
    return Response(storage.get(path), media_type=mimetypes.guess_type(path)[0])
