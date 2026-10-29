"""미디어 파일 기록과 업로드 검증 (supabase/migrations/0003_media_assets.sql, F03).

앱 업로드 흐름: POST /pets/:id/uploads (URL 발급) → 앱이 PUT으로 파일 업로드
              → POST /media/:id/finalize (크기 재확인 → media_check 작업 등록)
작업자는 load / save_generated / signed_url만 쓰면 된다.
"""
import uuid
from dataclasses import dataclass
from uuid import UUID

from app.config import get_settings
from app.db import get_pool
from app.errors import AppError, ErrorCode
from app.storage import get_storage

MB = 1024 * 1024


@dataclass(frozen=True)
class Kind:
    max_bytes: int
    max_count: int


# 반려견 1마리당 업로드 상한. 파일럿 기준이라 바뀌면 여기만 고친다.
# 장당 상한은 Supabase Free 플랜의 파일당 최대 50MB(버킷 상한)를 넘을 수 없다.
KINDS = {
    "image": Kind(max_bytes=10 * MB, max_count=10),
    "video": Kind(max_bytes=50 * MB, max_count=3),
    "audio": Kind(max_bytes=20 * MB, max_count=5),
}
# 종류별 상한을 다 채우면 350MB라, 전체 상한을 그보다 낮게 둬야 의미가 있다
TOTAL_BYTES = 200 * MB

EXTENSIONS = {
    "image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp",
    "image/heic": ".heic", "image/heif": ".heif",
    "video/mp4": ".mp4", "video/quicktime": ".mov",
    "audio/mp4": ".m4a", "audio/mpeg": ".mp3", "audio/wav": ".wav", "audio/aac": ".aac",
    "model/gltf-binary": ".glb",
}
UPLOAD_MIMES = {m for m in EXTENSIONS if m.split("/")[0] in KINDS}

# URL만 받고 올리지 않은 기록은 이 시간이 지나면 잔여 용량 계산에서 뺀다
PENDING_HOURS = 2


def _kind(mime: str) -> str:
    return mime.split("/")[0]


def _path(pet_id: UUID, media_id: UUID, mime: str) -> str:
    return f"pets/{pet_id}/{media_id}{EXTENSIONS.get(mime, '')}"


def create_upload(user_id: UUID, pet_id: UUID, mime: str, size_bytes: int) -> tuple[dict, str]:
    """형식·용량·잔여 용량을 확인하고 pending 기록과 업로드 URL을 만든다."""
    if mime not in UPLOAD_MIMES:
        raise AppError(ErrorCode.VALIDATION_FAILED, f"지원하지 않는 형식: {mime}")
    kind = KINDS[_kind(mime)]
    if not 0 < size_bytes <= kind.max_bytes:
        raise AppError(ErrorCode.VALIDATION_FAILED,
                       f"파일 크기는 {kind.max_bytes // MB}MB 이하여야 합니다")

    media_id = uuid.uuid4()
    path = _path(pet_id, media_id, mime)
    with get_pool().connection() as conn, conn.transaction():
        # 같은 반려견에 동시에 업로드해도 상한을 넘지 않도록 반려견 행을 잠근다
        conn.execute("select 1 from pets where id = %s for update", (pet_id,))
        used = conn.execute(
            """
            select count(*) filter (where split_part(mime_type, '/', 1) = %s) as count,
                   coalesce(sum(size_bytes), 0) as bytes
            from media_assets
            where pet_id = %s and source_type = 'upload'
              and (status in ('uploaded', 'ready')
                   or (status = 'pending' and created_at > now() - make_interval(hours => %s)))
            """,
            (_kind(mime), pet_id, PENDING_HOURS),
        ).fetchone()
        if used["count"] >= kind.max_count:
            raise AppError(ErrorCode.LIMIT_EXCEEDED,
                           f"{_kind(mime)} 파일은 {kind.max_count}개까지 올릴 수 있습니다")
        if used["bytes"] + size_bytes > TOTAL_BYTES:
            raise AppError(ErrorCode.LIMIT_EXCEEDED,
                           f"남은 용량이 부족합니다 (전체 {TOTAL_BYTES // MB}MB)")
        row = conn.execute(
            """
            insert into media_assets (id, user_id, pet_id, source_type, path, mime_type, size_bytes)
            values (%s, %s, %s, 'upload', %s, %s, %s) returning *
            """,
            (media_id, user_id, pet_id, path, mime, size_bytes),
        ).fetchone()
    return row, get_storage().upload_url(path, mime)


def finalize_upload(media_id: UUID) -> dict:
    """실제로 올라간 파일 크기를 다시 확인하고 uploaded로 바꾼다. 상한을 넘으면 지우고 거절한다."""
    row = get(media_id)
    if row["source_type"] != "upload":
        raise AppError(ErrorCode.VALIDATION_FAILED, "업로드한 파일이 아닙니다")
    if row["status"] != "pending":
        return row  # 앱이 finalize를 다시 불러도 같은 결과
    size = get_storage().size(row["path"])
    if size is None:
        raise AppError(ErrorCode.VALIDATION_FAILED, "아직 업로드되지 않았습니다")
    if size > KINDS[_kind(row["mime_type"])].max_bytes:
        get_storage().delete(row["path"])
        _set_status(media_id, "rejected", size, ErrorCode.LIMIT_EXCEEDED)
        raise AppError(ErrorCode.LIMIT_EXCEEDED, "올린 파일이 허용 크기보다 큽니다")
    return _set_status(media_id, "uploaded", size)


def _set_status(media_id: UUID, status: str, size: int | None = None,
                error_code: str | None = None) -> dict:
    with get_pool().connection() as conn:
        return conn.execute(
            """
            update media_assets set status = %s, size_bytes = coalesce(%s, size_bytes),
                   error_code = %s, updated_at = now()
            where id = %s returning *
            """,
            (status, size, error_code, media_id),
        ).fetchone()


def get(media_id: UUID | str) -> dict:
    try:
        media_id = UUID(str(media_id))
    except ValueError as e:
        raise AppError(ErrorCode.NOT_FOUND, "파일이 없습니다") from e
    with get_pool().connection() as conn:
        row = conn.execute("select * from media_assets where id = %s", (media_id,)).fetchone()
    if row is None:
        raise AppError(ErrorCode.NOT_FOUND, "파일이 없습니다")
    return row


def is_owner(media_id: UUID, user_id: UUID) -> bool:
    with get_pool().connection() as conn:
        row = conn.execute(
            """
            select 1 from media_assets m join pets p on p.id = m.pet_id
            where m.id = %s and p.user_id = %s
            """,
            (media_id, user_id),
        ).fetchone()
    return row is not None


def download_url(row: dict) -> str | None:
    """앱에 내려줄 짧은 만료 URL. 거절됐거나 아직 올라오지 않은 파일은 None."""
    if row["status"] not in ("uploaded", "ready"):
        return None
    return get_storage().download_url(row["path"], get_settings().signed_url_seconds)


# ---- 작업자용 ----

def load(media_id: UUID | str, pet_id: UUID | str) -> tuple[bytes, str]:
    """파일 내용과 MIME. 없거나, 다른 반려견 것이거나, 거절된 파일이면 VALIDATION_FAILED."""
    try:
        row = get(media_id)
    except AppError as e:
        raise AppError(ErrorCode.VALIDATION_FAILED, f"파일이 없음: {media_id}") from e
    if str(row["pet_id"]) != str(pet_id):
        raise AppError(ErrorCode.VALIDATION_FAILED, f"파일이 없음: {media_id}")
    if row["status"] not in ("uploaded", "ready"):
        raise AppError(ErrorCode.VALIDATION_FAILED, f"사용할 수 없는 파일: {media_id}")
    return get_storage().get(row["path"]), row["mime_type"]


def save_generated(user_id: UUID | str, pet_id: UUID | str, data: bytes, mime: str,
                   source_type: str, parent_id: UUID | str | None = None) -> str:
    """작업자가 만든 파일(4면 이미지, GLB, 썸네일)을 저장하고 ready 기록을 만든다."""
    media_id = uuid.uuid4()
    path = _path(UUID(str(pet_id)), media_id, mime)
    get_storage().put(path, data, mime)
    with get_pool().connection() as conn:
        conn.execute(
            """
            insert into media_assets
                (id, user_id, pet_id, source_type, parent_id, path, mime_type, size_bytes, status)
            values (%s, %s, %s, %s, %s, %s, %s, %s, 'ready')
            """,
            (media_id, user_id, pet_id, source_type, parent_id, path, mime, len(data)),
        )
    return str(media_id)


def signed_url(media_id: UUID | str, seconds: int | None = None) -> str:
    """외부 API(Meshy 등)에 넘길 서명 URL."""
    row = get(media_id)
    return get_storage().download_url(row["path"], seconds or get_settings().signed_url_seconds)
