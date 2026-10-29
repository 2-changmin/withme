"""나의 방 물건 API: 장난감·간식·애착자리 (#76). 처리는 app/items.py."""
from fastapi import APIRouter, Header

from app import items
from app.auth import CurrentUser
from app.ownership import OwnedPet
from app.schemas import Item, ItemAccepted, ItemCreate, ItemKind

router = APIRouter(tags=["items"])


@router.post("/pets/{pet_id}/items", response_model=ItemAccepted, status_code=202)
def create_item(pet_id: OwnedPet, body: ItemCreate, user_id: CurrentUser,
                idempotency_key: str | None = Header(None)) -> ItemAccepted:
    """물건 사진 1장 → 누끼(투명 배경 PNG) 작업을 시작한다.

    장난감은 누끼까지만 만들고 가챠를 기다린다. 간식·애착자리는 누끼 뒤 바로 4면·3D로 이어진다.
    같은 Idempotency-Key로 다시 보내면 같은 물건.
    """
    item_id, job_id = items.create(user_id, pet_id, body.kind, body.source_media_id,
                                   idempotency_key)
    return ItemAccepted(item_id=item_id, job_id=job_id)


@router.get("/pets/{pet_id}/items", response_model=list[Item])
def list_items(pet_id: OwnedPet, kind: ItemKind | None = None) -> list[Item]:
    """물건 목록 (올린 순서). status: cutting → cutout → generating → ready.

    가챠 버튼은 kind=toy, status=cutout인 장난감이 4개 이상일 때 켠다.
    """
    return [Item.model_validate(r) for r in items.list_items(pet_id, kind)]


@router.post("/pets/{pet_id}/toys/gacha", response_model=ItemAccepted, status_code=202)
def gacha(pet_id: OwnedPet, user_id: CurrentUser,
          idempotency_key: str | None = Header(None)) -> ItemAccepted:
    """아직 3D가 아닌 장난감 중 무작위 1개 → 4면·3D 생성 작업을 시작한다.

    후보가 4개 미만이면 409 CONFLICT. 포인트 차감은 앱 화면 연출만 한다.
    같은 Idempotency-Key로 다시 누르면 같은 장난감 (중복 청구 없음).
    """
    item_id, job_id = items.gacha(user_id, pet_id, idempotency_key)
    return ItemAccepted(item_id=item_id, job_id=job_id)
