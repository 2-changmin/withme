"""API와 작업자가 같이 쓰는 데이터 형식. Unity DTO는 /openapi.json 기준으로 만든다."""
from datetime import datetime
from enum import StrEnum
from typing import Annotated, Any
from uuid import UUID

from pydantic import BaseModel, Field, StringConstraints, model_validator

Vec3 = tuple[float, float, float]

# 왼쪽만 받고 오른쪽은 x를 뒤집어 만든다 (docs/03-3d-pipeline.md)
JOINT_NAMES = (
    "nose", "neck", "tail_base", "tail_tip",
    "front_paw_l", "shoulder_l", "hind_paw_l", "hip_l",
    "ear_tip_l", "ear_base_l",
)


class ErrorDetail(BaseModel):
    code: str = Field(description="UNAUTHORIZED·FORBIDDEN·NOT_FOUND·VALIDATION_FAILED·CONFLICT·"
                                  "LIMIT_EXCEEDED·UPSTREAM_FAILED·NOT_IMPLEMENTED·INTERNAL")
    message: str = Field(description="개발자 확인용. 화면 문구는 code로 고른다")


class ErrorResponse(BaseModel):
    """모든 오류 응답의 형식 (app/errors.py)."""
    error: ErrorDetail


class Joints(BaseModel):
    skeleton_version: int = Field(1, alias="skeletonVersion")
    space: str = "normalized"
    joints: dict[str, Vec3]

    model_config = {"populate_by_name": True, "serialize_by_alias": True}

    @model_validator(mode="after")
    def _all_joints(self) -> "Joints":
        missing = set(JOINT_NAMES) - self.joints.keys()
        if missing:
            raise ValueError(f"관절 누락: {sorted(missing)}")
        return self


class CharacterParams(BaseModel):
    ear_scale: float = Field(1.0, alias="earScale", ge=0.5, le=1.5)
    ear_angle: float = Field(0.0, alias="earAngle", ge=-45, le=45)
    tail_scale: float = Field(1.0, alias="tailScale", ge=0.5, le=1.5)
    leg_length: float = Field(1.0, alias="legLength", ge=0.7, le=1.3)
    body_length: float = Field(1.0, alias="bodyLength", ge=0.7, le=1.3)
    head_scale: float = Field(1.0, alias="headScale", ge=0.8, le=1.2)
    hue_shift: float = Field(0.0, alias="hueShift", ge=-30, le=30)
    brightness: float = Field(0.0, ge=-0.3, le=0.3)
    accessory: str | None = None

    model_config = {"populate_by_name": True}


class Character(BaseModel):
    model_ref: str = Field(alias="modelRef")
    skeleton_version: int = Field(1, alias="skeletonVersion")
    params: CharacterParams = CharacterParams()

    model_config = {"populate_by_name": True}


class JobType(StrEnum):
    ECHO = "echo"                        # 파이프라인 점검용
    MEDIA_CHECK = "media_check"          # 박재현
    REFERENCE_VIEWS = "reference_views"  # 윤승희
    MESHY_GENERATE = "meshy_generate"    # 이창민
    MESH_PREPARE = "mesh_prepare"        # 황세인
    RIG = "rig"                          # 황세인
    SOUND_SPLIT = "sound_split"          # 박재현


class JobStatus(StrEnum):
    QUEUED = "queued"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"


class Job(BaseModel):
    id: UUID | str
    type: JobType
    status: JobStatus
    progress: int = 0
    result: dict[str, Any] | None = None
    error_code: str | None = Field(None, alias="errorCode")
    error_message: str | None = Field(None, alias="errorMessage")
    created_at: datetime | None = Field(None, alias="createdAt")
    finished_at: datetime | None = Field(None, alias="finishedAt")

    model_config = {"populate_by_name": True, "serialize_by_alias": True}


class ReferenceViewCreate(BaseModel):
    """원본 사진 media ID (POST /pets/:id/uploads → finalize로 받은 것) 1~4장."""
    source_media_ids: list[UUID] = Field(alias="sourceMediaIds", min_length=1, max_length=4)

    model_config = {"populate_by_name": True}


class JobAccepted(BaseModel):
    job_id: UUID | str = Field(alias="jobId")

    model_config = {"populate_by_name": True, "serialize_by_alias": True}


PetName = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=30)]


class PetCreate(BaseModel):
    name: PetName


class PetUpdate(BaseModel):
    name: PetName | None = None


class Pet(BaseModel):
    id: UUID
    name: str
    photo_media_id: UUID | None = Field(None, alias="photoMediaId")
    created_at: datetime = Field(alias="createdAt")
    updated_at: datetime = Field(alias="updatedAt")

    model_config = {"populate_by_name": True, "serialize_by_alias": True}


class UploadCreate(BaseModel):
    mime_type: str = Field(alias="mimeType")
    size_bytes: int = Field(alias="sizeBytes", gt=0)

    model_config = {"populate_by_name": True}


class UploadTicket(BaseModel):
    """앱은 uploadUrl에 headers를 붙여 PUT으로 파일 본문을 올린 뒤 finalize를 부른다."""
    media_id: UUID = Field(alias="mediaId")
    upload_url: str = Field(alias="uploadUrl")
    method: str = "PUT"
    headers: dict[str, str]

    model_config = {"populate_by_name": True, "serialize_by_alias": True}


class MediaStatus(StrEnum):
    PENDING = "pending"    # 업로드 URL만 발급
    UPLOADED = "uploaded"  # 업로드 완료, 검사 중
    READY = "ready"        # 사용 가능
    REJECTED = "rejected"  # 검사에서 거절 (errorCode 참고)


class Media(BaseModel):
    id: UUID
    pet_id: UUID = Field(alias="petId")
    source_type: str = Field(alias="sourceType")
    mime_type: str = Field(alias="mimeType")
    size_bytes: int = Field(alias="sizeBytes")
    status: MediaStatus
    error_code: str | None = Field(None, alias="errorCode")
    # 짧은 만료 서명 URL (SIGNED_URL_SECONDS). 만료되면 다시 조회한다.
    url: str | None = None
    created_at: datetime = Field(alias="createdAt")

    model_config = {"populate_by_name": True, "serialize_by_alias": True}
