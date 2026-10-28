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
    meshy_api_key: str = ""

    # 작업자
    worker_id: str = "worker-local"
    worker_poll_seconds: float = 2.0
    worker_lease_seconds: int = 300
    blender_path: str = "blender"
    blender_scripts_dir: str = "../blender"


@lru_cache
def get_settings() -> Settings:
    return Settings()
