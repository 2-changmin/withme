"""반려견 프로필·동의 API (docs/04-api-data.md, F02)."""
from fastapi import APIRouter

from app import pets
from app.auth import CurrentUser
from app.ownership import OwnedPet
from app.schemas import Consent, ConsentCreate, Pet, PetCreate, PetUpdate

router = APIRouter(tags=["pets"])


@router.post("/pets", response_model=Pet, status_code=201)
def create_pet(body: PetCreate, user_id: CurrentUser) -> Pet:
    """반려견 등록. 계정당 1마리이고, 이미 있으면 409 CONFLICT."""
    return Pet.model_validate(pets.create(user_id, body.name))


@router.get("/pets", response_model=list[Pet])
def list_pets(user_id: CurrentUser) -> list[Pet]:
    """내 반려견 목록 (0~1마리). 재로그인 후 pet_id를 다시 찾을 때 쓴다."""
    return [Pet.model_validate(row) for row in pets.list_for_user(user_id)]


@router.get("/pets/{pet_id}", response_model=Pet)
def get_pet(pet_id: OwnedPet) -> Pet:
    return Pet.model_validate(pets.get(pet_id))


@router.patch("/pets/{pet_id}", response_model=Pet)
def update_pet(pet_id: OwnedPet, body: PetUpdate) -> Pet:
    """프로필 수정. 보내지 않은 항목은 그대로 둔다."""
    return Pet.model_validate(pets.update(pet_id, body.name))


@router.post("/pets/{pet_id}/consents", response_model=list[Consent], status_code=201)
def create_consents(pet_id: OwnedPet, body: ConsentCreate, user_id: CurrentUser) -> list[Consent]:
    """동의 기록 (Gemini·Meshy 전달). 기록은 쌓이고, 응답은 항목별 최신 동의다."""
    pets.add_consents(user_id, pet_id, body.items, body.version)
    return [Consent.model_validate(row) for row in pets.latest_consents(pet_id)]


@router.get("/pets/{pet_id}/consents", response_model=list[Consent])
def list_consents(pet_id: OwnedPet) -> list[Consent]:
    """항목별 최신 동의. 동의하지 않은 항목은 목록에 없다."""
    return [Consent.model_validate(row) for row in pets.latest_consents(pet_id)]
