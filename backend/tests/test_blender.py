"""Blender 헤드리스 실행 테스트. blender 실행 파일이 있을 때만 실행된다 (작업자 컨테이너 등)."""
import shutil

import pytest

from app.config import get_settings
from app.errors import AppError, ErrorCode
from app.worker.blender import run_blender

pytestmark = pytest.mark.skipif(
    shutil.which(get_settings().blender_path) is None, reason="Blender 없음"
)


def test_blender_exports_glb(tmp_path):
    out = tmp_path / "cube.glb"
    stdout = run_blender("check.py", str(out))

    assert "CHECK_OK" in stdout
    assert out.read_bytes()[:4] == b"glTF"


def test_script_error_raises(tmp_path):
    # 출력 경로의 폴더가 없으면 내보내기가 예외로 끝난다
    with pytest.raises(AppError) as e:
        run_blender("check.py", str(tmp_path / "없는폴더" / "cube.glb"))
    assert e.value.code == ErrorCode.INTERNAL
