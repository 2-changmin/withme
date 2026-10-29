"""Supabase 액세스 토큰 검증. 라우트에서 `user_id: CurrentUser`로 받는다."""
from functools import lru_cache
from typing import Annotated
from uuid import UUID

import jwt
from fastapi import Depends, Header
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

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


# Swagger(/docs)의 Authorize 버튼이 이 방식을 쓴다. 토큰이 없어도 여기서 막지 않고 아래에서 401로 답한다.
_bearer = HTTPBearer(auto_error=False, description="Supabase access token (docs/auth-for-unity.md)")


def current_user(
    bearer: Annotated[HTTPAuthorizationCredentials | None, Depends(_bearer)] = None,
    # 로컬 개발 전용 우회 (ENV=local + AUTH_DEV_BYPASS). 앱은 쓰지 않아서 명세에서 숨긴다
    x_dev_user: Annotated[str | None, Header(include_in_schema=False)] = None,
) -> UUID:
    s = get_settings()
    if s.auth_dev_bypass and s.env == "local" and x_dev_user:
        try:
            return UUID(x_dev_user)
        except ValueError as e:
            raise AppError(ErrorCode.UNAUTHORIZED, "X-Dev-User는 UUID여야 합니다") from e
    if bearer is None:
        raise AppError(ErrorCode.UNAUTHORIZED, "Bearer 토큰이 필요합니다")
    try:
        claims = _decode(bearer.credentials)
        return UUID(claims["sub"])
    except (jwt.PyJWTError, KeyError, ValueError) as e:
        raise AppError(ErrorCode.UNAUTHORIZED, f"토큰 검증 실패: {e}") from e


CurrentUser = Annotated[UUID, Depends(current_user)]
