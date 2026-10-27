"""Blender 헤드리스 실행: blender -b -P <script> -- <args...>"""
import subprocess
from pathlib import Path

from app.config import get_settings
from app.errors import AppError, ErrorCode


def run_blender(script: str, *args: str, timeout: int = 120) -> str:
    """blender/ 폴더의 스크립트를 실행하고 표준 출력을 돌려준다."""
    s = get_settings()
    script_path = Path(s.blender_scripts_dir) / script
    if not script_path.exists():
        raise AppError(ErrorCode.INTERNAL, f"Blender 스크립트 없음: {script_path}")
    cmd = [s.blender_path, "-b", "--factory-startup", "-P", str(script_path), "--", *args]
    try:
        r = subprocess.run(cmd, check=False, capture_output=True, text=True, timeout=timeout)
    except FileNotFoundError as e:
        raise AppError(ErrorCode.INTERNAL, f"Blender 실행 파일 없음: {s.blender_path}") from e
    except subprocess.TimeoutExpired as e:
        raise AppError(ErrorCode.INTERNAL, f"Blender 시간 초과 ({timeout}초)") from e
    if r.returncode != 0:
        raise AppError(ErrorCode.INTERNAL, f"Blender 실패: {r.stderr[-800:]}")
    return r.stdout
