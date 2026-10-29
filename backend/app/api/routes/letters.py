"""무지개다리 편지 API (#74, docs/decisions/2026-10-30-rainbow-letter.md)."""
from fastapi import APIRouter, Response

from app import letters
from app.auth import CurrentUser
from app.ownership import OwnedPet
from app.schemas import Letter, LetterCreate

router = APIRouter(tags=["letters"])


@router.post("/pets/{pet_id}/letters", response_model=Letter, status_code=201)
def create_letter(pet_id: OwnedPet, body: LetterCreate, user_id: CurrentUser) -> Letter:
    """편지 쓰기. 답장은 작업(letter_reply)으로 쓰고, jobId나 GET /letters/{id}로 확인한다.

    - 하루 1통 (한국 시간 기준). 넘으면 429 LIMIT_EXCEEDED
    - 위기 표현이 있으면 답장 없이 status=crisis와 상담 안내(help)를 바로 돌려준다
    """
    return letters.to_model(letters.create(user_id, pet_id, body.body))


@router.get("/pets/{pet_id}/letters", response_model=list[Letter])
def list_letters(pet_id: OwnedPet) -> list[Letter]:
    """편지함. 최근 편지부터."""
    return [letters.to_model(row) for row in letters.list_for_pet(pet_id)]


@router.get("/letters/{letter_id}", response_model=Letter)
def get_letter(letter_id: str, user_id: CurrentUser) -> Letter:
    """편지 하나. 답장을 기다릴 때 3초마다 조회한다 (status가 pending이 아닐 때까지)."""
    return letters.to_model(letters.get(user_id, letter_id))


@router.delete("/letters/{letter_id}", status_code=204)
def delete_letter(letter_id: str, user_id: CurrentUser) -> Response:
    """편지와 답장을 지운다."""
    letters.delete(user_id, letter_id)
    return Response(status_code=204)
