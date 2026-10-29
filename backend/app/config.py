from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    env: str = "local"
    database_url: str = "postgresql://postgres:postgres@localhost:54322/postgres"

    # Supabase 인증. JWT 시크릿이 있으면 HS256, 없으면 JWKS(비대칭 키)로 검증한다.
    supabase_url: str = ""
    supabase_jwt_secret: str = ""
    # 로컬 개발 전용: X-Dev-User 헤더의 UUID를 로그인 사용자로 취급한다.
    auth_dev_bypass: bool = False

    # 캐릭터 API를 목 응답으로 돌려준다 (#19). 실제 연결(#23)이 끝나면 false.
    mock_character: bool = True

    # 외부 API (작업자 전용)
    gemini_api_key: str = ""
    # 이미지 모델 ID는 자주 바뀐다. 최신 ID는 ai.google.dev 모델 목록에서 확인 (#38)
    gemini_image_model: str = "gemini-2.5-flash-image"
    # true면 Gemini를 부르지 않고 원본 사진을 그대로 4면 결과로 돌려준다 (비용 0).
    # 실제로 돌려 볼 사람만 자기 .env에서 false로 바꾼다.
    mock_gemini: bool = True
    meshy_api_key: str = ""
    # Meshy를 부르지 않고 가짜 결과를 돌려준다 (크레딧 절약). 실제 생성이 필요할 때만 false.
    mock_meshy: bool = True

    # 미디어 저장소 (#30). local: LOCAL_MEDIA_DIR 폴더 + API가 서명 URL 처리 (ENV=local 전용)
    # supabase: 비공개 버킷. SUPABASE_URL과 서비스 키가 필요하다.
    storage_backend: str = "local"
    storage_bucket: str = "media"
    supabase_service_role_key: str = ""
    # 앱에 내려주는 서명 URL 만료 (초)
    signed_url_seconds: int = 300
    local_media_dir: str = ".media"
    # local 저장소의 서명 URL이 가리키는 API 주소와 서명 키
    public_base_url: str = "http://localhost:8000"
    local_storage_secret: str = "local-dev-only"

    # 작업자
    worker_id: str = "worker-local"
    worker_poll_seconds: float = 2.0
    worker_lease_seconds: int = 300
    blender_path: str = "blender"
    blender_scripts_dir: str = "../blender"


@lru_cache
def get_settings() -> Settings:
    return Settings()
