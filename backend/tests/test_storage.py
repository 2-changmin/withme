"""Supabase 저장소 요청 형식. 실제 Supabase 대신 httpx.MockTransport로 확인한다."""
import json

import httpx
import pytest

from app.errors import AppError, ErrorCode
from app.storage import SupabaseStorage

BASE = "https://proj.supabase.co/storage/v1"


def make(handler) -> tuple[SupabaseStorage, list[httpx.Request]]:
    seen = []

    def record(req: httpx.Request) -> httpx.Response:
        seen.append(req)
        return handler(req)

    return SupabaseStorage("https://proj.supabase.co/", "svc", "media",
                           transport=httpx.MockTransport(record)), seen


def test_signed_urls():
    def handler(req):
        if "/upload/sign/" in req.url.path:
            return httpx.Response(200, json={"url": "/object/upload/sign/media/pets/a.jpg?token=u"})
        return httpx.Response(200, json={"signedURL": "/object/sign/media/pets/a.jpg?token=d"})

    s, seen = make(handler)
    assert s.upload_url("pets/a.jpg", "image/jpeg") == \
        f"{BASE}/object/upload/sign/media/pets/a.jpg?token=u"
    assert s.download_url("pets/a.jpg", 300) == f"{BASE}/object/sign/media/pets/a.jpg?token=d"
    assert [str(r.url) for r in seen] == [f"{BASE}/object/upload/sign/media/pets/a.jpg",
                                          f"{BASE}/object/sign/media/pets/a.jpg"]
    assert seen[1].read() == b'{"expiresIn":300}'
    assert all(r.headers["authorization"] == "Bearer svc" for r in seen)


def test_put_get_size_delete():
    store = {}

    def handler(req):
        if req.url.path == "/storage/v1/object/list/media":
            q = json.loads(req.read())
            return httpx.Response(200, json=[
                {"name": p.rpartition("/")[2], "metadata": {"size": len(d)}}
                for p, d in store.items()
                if p.rpartition("/")[0] == q["prefix"] and q["search"] in p])
        path = req.url.path.removeprefix("/storage/v1/object/media/")
        if req.method == "POST":
            store[path] = req.read()
            return httpx.Response(200, json={})
        if req.method == "DELETE":
            store.pop(path, None)
            return httpx.Response(200, json={})
        if path not in store:
            return httpx.Response(400, json={"error": "not_found"})
        return httpx.Response(200, content=store[path])

    s, seen = make(handler)
    s.put("pets/a.png", b"png", "image/png")
    assert seen[0].headers["content-type"] == "image/png"
    assert "x-upsert" not in seen[0].headers  # 덮어쓰지 않는다 (CDN 캐시)
    s.put("pets/ab.png", b"other", "image/png")
    assert s.get("pets/a.png") == b"png"
    assert s.size("pets/a.png") == 3
    s.delete("pets/a.png")
    assert s.size("pets/a.png") is None
    s.delete("pets/a.png")  # 이미 없어도 오류 아님


def test_server_error_is_retryable():
    s, _ = make(lambda req: httpx.Response(503))
    with pytest.raises(AppError) as e:
        s.get("pets/a.png")
    assert e.value.code == ErrorCode.UPSTREAM_FAILED
