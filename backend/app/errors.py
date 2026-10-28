"""공통 오류 규약: 모든 오류 응답은 {"error": {"code", "message"}} 형식이다.

Unity는 code를 보고 화면 문구를 고른다. message는 개발자 확인용이다.
"""
import logging
from enum import StrEnum

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

logger = logging.getLogger(__name__)


class ErrorCode(StrEnum):
    UNAUTHORIZED = "UNAUTHORIZED"
    FORBIDDEN = "FORBIDDEN"
    NOT_FOUND = "NOT_FOUND"
    VALIDATION_FAILED = "VALIDATION_FAILED"
    CONFLICT = "CONFLICT"
    LIMIT_EXCEEDED = "LIMIT_EXCEEDED"
    UPSTREAM_FAILED = "UPSTREAM_FAILED"
    NOT_IMPLEMENTED = "NOT_IMPLEMENTED"
    INTERNAL = "INTERNAL"


STATUS = {
    ErrorCode.UNAUTHORIZED: 401,
    ErrorCode.FORBIDDEN: 403,
    ErrorCode.NOT_FOUND: 404,
    ErrorCode.VALIDATION_FAILED: 422,
    ErrorCode.CONFLICT: 409,
    ErrorCode.LIMIT_EXCEEDED: 429,
    ErrorCode.UPSTREAM_FAILED: 502,
    ErrorCode.NOT_IMPLEMENTED: 501,
    ErrorCode.INTERNAL: 500,
}


_CODE_BY_STATUS = {status: code for code, status in STATUS.items()}


class AppError(Exception):
    def __init__(self, code: ErrorCode, message: str = ""):
        super().__init__(message or code)
        self.code = code
        self.message = message or code


def _body(code: ErrorCode, message: str) -> dict:
    return {"error": {"code": code, "message": message}}


def install_error_handlers(app: FastAPI) -> None:
    @app.exception_handler(AppError)
    async def app_error(_: Request, e: AppError):
        return JSONResponse(_body(e.code, e.message), status_code=STATUS[e.code])

    @app.exception_handler(RequestValidationError)
    async def validation_error(_: Request, e: RequestValidationError):
        return JSONResponse(_body(ErrorCode.VALIDATION_FAILED, str(e.errors()[:3])), status_code=422)

    @app.exception_handler(StarletteHTTPException)
    async def http_error(_: Request, e: StarletteHTTPException):
        code = _CODE_BY_STATUS.get(e.status_code, ErrorCode.INTERNAL)
        return JSONResponse(_body(code, str(e.detail)), status_code=e.status_code)

    @app.exception_handler(Exception)
    async def unhandled_error(_: Request, e: Exception):
        # 내부 사정(SQL, 경로 등)은 응답에 싣지 않는다. 원인은 서버 로그로 확인한다.
        logger.exception("처리하지 못한 오류")
        return JSONResponse(_body(ErrorCode.INTERNAL, "서버 오류"), status_code=500)
