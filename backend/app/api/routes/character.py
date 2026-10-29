"""캐릭터 생성 API (docs/04-api-data.md, F04~F07).

실제 처리는 app/characters.py (테이블 + 작업 큐, #23). MOCK_CHARACTER=true면 예전 목 응답(#19)을
돌려준다. 황세인이 화면을 만들 때 쓰고, 응답 형식은 실제와 같다
(작업 결과의 media ID는 GET /jobs/:id가 서명 URL로 바꿔 views·glbUrl 등에 담는다).
"""
from uuid import UUID, uuid4

from fastapi import APIRouter, Header

from app import characters
from app.auth import CurrentUser
from app.config import get_settings
from app.errors import AppError, ErrorCode
from app.ownership import require_pet_owner
from app.schemas import Job, JobAccepted, JobStatus, JobType, Joints, ReferenceViewCreate

router = APIRouter(tags=["character"])

MOCK_PREFIX = "mock-"
SAMPLE_JOINTS = Joints(joints={
    "nose": (0.00, 0.42, 0.61), "neck": (0.00, 0.40, 0.38),
    "tail_base": (0.00, 0.37, -0.35), "tail_tip": (0.00, 0.48, -0.58),
    "front_paw_l": (0.09, 0.00, 0.30), "shoulder_l": (0.08, 0.30, 0.30),
    "hind_paw_l": (0.09, 0.00, -0.28), "hip_l": (0.08, 0.31, -0.27),
    "ear_tip_l": (0.07, 0.62, 0.45), "ear_base_l": (0.05, 0.52, 0.42),
})

# 목 작업 ID 접두어 → 완료 시 결과
_MOCK_RESULTS = {
    JobType.REFERENCE_VIEWS: lambda: {
        "referenceViewId": f"{MOCK_PREFIX}rv",
        "views": [f"https://placehold.co/1024x1024?text={v}" for v in ("side", "front", "back", "other")],
    },
    JobType.MESHY_GENERATE: lambda: {"characterModelId": f"{MOCK_PREFIX}cm",
                                     "glbUrl": "https://example.com/mock/raw.glb"},
    JobType.RIG: lambda: {"characterModelId": f"{MOCK_PREFIX}cm",
                          "riggedGlbUrl": "https://example.com/mock/rigged.glb"},
}


def _mock() -> bool:
    return get_settings().mock_character


def _mock_accept(job_type: JobType) -> JobAccepted:
    return JobAccepted(job_id=f"{MOCK_PREFIX}{job_type}-{uuid4().hex[:8]}")


def mock_job(job_id: str) -> Job:
    job_type = next((t for t in _MOCK_RESULTS if job_id.startswith(f"{MOCK_PREFIX}{t}-")), None)
    if job_type is None:
        raise AppError(ErrorCode.NOT_FOUND, "작업이 없습니다")
    return Job(id=job_id, type=job_type, status=JobStatus.SUCCEEDED, progress=100,
               result=_MOCK_RESULTS[job_type]())


@router.post("/pets/{pet_id}/reference-views", response_model=JobAccepted, status_code=202)
def create_reference_views(pet_id: UUID, user_id: CurrentUser,
                           body: ReferenceViewCreate | None = None,
                           idempotency_key: str | None = Header(None)) -> JobAccepted:
    """원본 사진(1~4장)으로 4면 이미지 생성 작업을 시작한다.

    같은 Idempotency-Key로 다시 보내면 같은 작업.
    """
    if _mock():
        return _mock_accept(JobType.REFERENCE_VIEWS)
    require_pet_owner(pet_id, user_id)
    if body is None:
        raise AppError(ErrorCode.VALIDATION_FAILED, "sourceMediaIds가 필요합니다")
    return JobAccepted(job_id=characters.create_reference_views(
        user_id, pet_id, body.source_media_ids, idempotency_key))


@router.post("/reference-views/{reference_view_id}/retry", response_model=JobAccepted,
             status_code=202)
def retry_reference_views(reference_view_id: str, user_id: CurrentUser) -> JobAccepted:
    """4면 이미지 재생성 (최대 3회, 넘으면 429 LIMIT_EXCEEDED). 같은 시도를 두 번 눌러도 한 번만."""
    if _mock():
        return _mock_accept(JobType.REFERENCE_VIEWS)
    return JobAccepted(job_id=characters.retry_reference_views(user_id, reference_view_id))


@router.post("/reference-views/{reference_view_id}/approve", response_model=JobAccepted,
             status_code=202)
def approve_reference_views(reference_view_id: str, user_id: CurrentUser,
                            idempotency_key: str | None = Header(None)) -> JobAccepted:
    """4면 확인 → 3D 생성(Meshy) 작업을 시작한다.

    4면이 아직이면 409 CONFLICT.
    4면 1세트에 3D 생성은 한 번만이라 다시 눌러도 같은 작업을 돌려준다.
    """
    if _mock():
        return _mock_accept(JobType.MESHY_GENERATE)
    return JobAccepted(job_id=characters.approve_reference_views(user_id, reference_view_id))


@router.get("/character-models/{character_model_id}/joints", response_model=Joints)
def get_joints(character_model_id: str, user_id: CurrentUser) -> Joints:
    """자동 추정한 관절 좌표. 메시 정리(mesh_prepare)가 끝나기 전에는 409 CONFLICT."""
    if _mock():
        return SAMPLE_JOINTS
    return characters.get_joints(user_id, character_model_id)


@router.post("/character-models/{character_model_id}/rig", response_model=JobAccepted,
             status_code=202)
def rig(character_model_id: str, joints: Joints, user_id: CurrentUser,
        idempotency_key: str | None = Header(None)) -> JobAccepted:
    """보정된 관절 좌표로 리깅 작업을 시작한다. 관절을 다시 지정하면 다시 리깅할 수 있다."""
    if _mock():
        return _mock_accept(JobType.RIG)
    return JobAccepted(job_id=characters.rig(user_id, character_model_id, joints,
                                             idempotency_key))
