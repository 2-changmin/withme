"""pet_id 소유권 재검증. 클라이언트가 보낸 pet_id는 믿지 않고 매 요청마다 DB로 확인한다.

라우트에서 `pet_id: OwnedPet`으로 받으면 경로의 {pet_id}가 로그인 사용자 소유인지 확인된 값이 들어온다.
남의 반려견이든 없는 반려견이든 똑같이 404로 답해서 존재 여부도 노출하지 않는다 (T09).
"""
from typing import Annotated
from uuid import UUID

from fastapi import Depends

from app.auth import CurrentUser
from app.db import get_pool
from app.errors import AppError, ErrorCode


def is_pet_owner(pet_id: UUID, user_id: UUID) -> bool:
    with get_pool().connection() as conn:
        row = conn.execute(
            "select 1 from pets where id = %s and user_id = %s", (pet_id, user_id)
        ).fetchone()
    return row is not None


def require_pet_owner(pet_id: UUID, user_id: UUID) -> None:
    if not is_pet_owner(pet_id, user_id):
        raise AppError(ErrorCode.NOT_FOUND, "반려견이 없습니다")


def owned_pet(pet_id: UUID, user_id: CurrentUser) -> UUID:
    require_pet_owner(pet_id, user_id)
    return pet_id


OwnedPet = Annotated[UUID, Depends(owned_pet)]
