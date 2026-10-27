"""Supabase 액세스 토큰 검증. 라우트에서 `user_id: CurrentUser`로 받는다."""
from functools import lru_cache
from typing import Annotated
from uuid import UUID

import jwt
from fastapi import Depends, Header

from app.config import get_settings
from app.errors import AppError, ErrorCode


@lru_cache
def _jwks_client() -> jwt.PyJWKClient:
    url = get_settings().supabase_url.rstrip("/") + "/auth/v1/.well-known/jwks.json"
    return jwt.PyJWKClient(url)


def _decode(token: str) -> dict:
    s = get_settings()
    if s.supabase_jwt_secret:
        return jwt.decode(token, s.supabase_jwt_secret, algorithms=["HS256"],
                          audience="authenticated")
    key = _jwks_client().get_signing_key_from_jwt(token).key
    return jwt.decode(token, key, algorithms=["ES256", "RS256"], audience="authenticated")


def current_user(
    authorization: Annotated[str | None, Header()] = None,
    x_dev_user: Annotated[str | None, Header()] = None,
) -> UUID:
    s = get_settings()
    if s.auth_dev_bypass and s.env == "local" and x_dev_user:
        return UUID(x_dev_user)
    if not authorization or not authorization.startswith("Bearer "):
        raise AppError(ErrorCode.UNAUTHORIZED, "Bearer 토큰이 필요합니다")
    try:
        claims = _decode(authorization.removeprefix("Bearer "))
        return UUID(claims["sub"])
    except (jwt.PyJWTError, KeyError, ValueError) as e:
        raise AppError(ErrorCode.UNAUTHORIZED, f"토큰 검증 실패: {e}") from e


CurrentUser = Annotated[UUID, Depends(current_user)]
