"""미디어 파일 저장소 (#30). STORAGE_BACKEND로 고른다.

- supabase: 비공개 버킷. 서버는 서비스 키로 읽고 쓰고, 앱에는 서명 URL만 준다.
- local: LOCAL_MEDIA_DIR 폴더. 서명 URL은 이 API의 /storage/local/... 을 가리킨다 (ENV=local 전용).

경로는 버킷 안 경로 (예: pets/<pet_id>/<media_id>.jpg). DB에는 이 경로만 저장한다.
"""
import hashlib
import hmac
import time
from functools import lru_cache
from pathlib import Path
from typing import Protocol
from urllib.parse import quote, urlencode

import httpx

from app.config import get_settings
from app.errors import AppError, ErrorCode


class Storage(Protocol):
    def put(self, path: str, data: bytes, mime: str) -> None: ...
    def get(self, path: str) -> bytes: ...
    def size(self, path: str) -> int | None:
        """파일 크기. 없으면 None."""
    def delete(self, path: str) -> None: ...
    def upload_url(self, path: str, mime: str) -> str:
        """앱이 PUT으로 파일을 올릴 서명 URL."""
    def download_url(self, path: str, seconds: int) -> str: ...


class SupabaseStorage:
    def __init__(self, url: str, key: str, bucket: str,
                 transport: httpx.BaseTransport | None = None):
        self.base = url.rstrip("/") + "/storage/v1"
        self.bucket = bucket
        self.http = httpx.Client(timeout=30, transport=transport,
                                 headers={"Authorization": f"Bearer {key}", "apikey": key})

    def _object(self, path: str) -> str:
        return f"{self.base}/object/{self.bucket}/{quote(path)}"

    def _check(self, r: httpx.Response) -> httpx.Response:
        if r.status_code >= 500:
            raise AppError(ErrorCode.UPSTREAM_FAILED, f"저장소 오류 {r.status_code}")
        if r.status_code >= 400:
            raise AppError(ErrorCode.INTERNAL, f"저장소 요청 실패 {r.status_code}: {r.text[:200]}")
        return r

    # 파일 GET은 앞단 CDN이 캐시해서 덮어쓰기·삭제 뒤에도 옛 내용이 나온다.
    # 그래서 파일은 덮어쓰지 않고(경로마다 새 UUID), 크기는 캐시되지 않는 목록 조회로 확인한다.
    def put(self, path: str, data: bytes, mime: str) -> None:
        self._check(self.http.post(self._object(path), content=data,
                                   headers={"Content-Type": mime}))

    def get(self, path: str) -> bytes:
        return self._check(self.http.get(self._object(path))).content

    def size(self, path: str) -> int | None:
        folder, _, name = path.rpartition("/")
        r = self._check(self.http.post(f"{self.base}/object/list/{self.bucket}",
                                       json={"prefix": folder, "search": name, "limit": 100}))
        for obj in r.json():
            if obj["name"] == name and obj.get("metadata"):
                return int(obj["metadata"]["size"])
        return None

    def delete(self, path: str) -> None:
        r = self.http.delete(self._object(path))
        if r.status_code not in (400, 404):
            self._check(r)

    def upload_url(self, path: str, mime: str) -> str:
        # Supabase 서명 업로드 URL은 만료가 2시간으로 고정이다. 크기는 finalize에서 다시 확인한다.
        r = self._check(self.http.post(f"{self.base}/object/upload/sign/{self.bucket}/{quote(path)}"))
        return self.base + r.json()["url"]

    def download_url(self, path: str, seconds: int) -> str:
        r = self._check(self.http.post(f"{self.base}/object/sign/{self.bucket}/{quote(path)}",
                                       json={"expiresIn": seconds}))
        return self.base + r.json()["signedURL"]


class LocalStorage:
    UPLOAD_SECONDS = 2 * 60 * 60  # Supabase와 맞춘다

    def __init__(self, root: str, base_url: str, secret: str):
        self.root = Path(root)
        self.base_url = base_url.rstrip("/")
        self.secret = secret.encode()

    def _file(self, path: str) -> Path:
        f = (self.root / path).resolve()
        if not f.is_relative_to(self.root.resolve()):
            raise AppError(ErrorCode.VALIDATION_FAILED, "잘못된 경로")
        return f

    def put(self, path: str, data: bytes, mime: str) -> None:
        f = self._file(path)
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_bytes(data)

    def get(self, path: str) -> bytes:
        try:
            return self._file(path).read_bytes()
        except FileNotFoundError as e:
            raise AppError(ErrorCode.NOT_FOUND, f"파일이 없음: {path}") from e

    def size(self, path: str) -> int | None:
        f = self._file(path)
        return f.stat().st_size if f.exists() else None

    def delete(self, path: str) -> None:
        self._file(path).unlink(missing_ok=True)

    def sign(self, op: str, path: str, exp: int) -> str:
        return hmac.new(self.secret, f"{op}:{path}:{exp}".encode(), hashlib.sha256).hexdigest()

    def verify(self, op: str, path: str, exp: int, sig: str) -> bool:
        return exp >= time.time() and hmac.compare_digest(self.sign(op, path, exp), sig)

    def _url(self, op: str, path: str, seconds: int) -> str:
        exp = int(time.time()) + seconds
        query = urlencode({"exp": exp, "sig": self.sign(op, path, exp)})
        return f"{self.base_url}/storage/local/{quote(path)}?{query}"

    def upload_url(self, path: str, mime: str) -> str:
        return self._url("put", path, self.UPLOAD_SECONDS)

    def download_url(self, path: str, seconds: int) -> str:
        return self._url("get", path, seconds)


@lru_cache
def get_storage() -> Storage:
    s = get_settings()
    if s.storage_backend == "supabase":
        if not (s.supabase_url and s.supabase_service_role_key):
            raise AppError(ErrorCode.INTERNAL, "SUPABASE_URL·SUPABASE_SERVICE_ROLE_KEY가 필요합니다")
        return SupabaseStorage(s.supabase_url, s.supabase_service_role_key, s.storage_bucket)
    if s.storage_backend == "local":
        if s.env != "local":
            raise AppError(ErrorCode.INTERNAL, "local 저장소는 ENV=local에서만 씁니다")
        return LocalStorage(s.local_media_dir, s.public_base_url, s.local_storage_secret)
    raise AppError(ErrorCode.INTERNAL, f"알 수 없는 STORAGE_BACKEND: {s.storage_backend}")
