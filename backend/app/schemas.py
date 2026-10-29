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


class ConsentItem(StrEnum):
    GEMINI_TRANSFER = "gemini_transfer"  # 사진을 Gemini(Google)에 보내 4면 이미지 생성
    MESHY_TRANSFER = "meshy_transfer"    # 4면 이미지를 Meshy에 보내 3D 생성


class ConsentCreate(BaseModel):
    items: list[ConsentItem] = Field(min_length=1)
    # 동의 화면 문구 버전 (#22). 문구가 바뀌면 올린다.
    version: str = Field(min_length=1, max_length=40)


class Consent(BaseModel):
    item: ConsentItem
    version: str
    agreed_at: datetime = Field(alias="agreedAt")

    model_config = {"populate_by_name": True, "serialize_by_alias": True}
