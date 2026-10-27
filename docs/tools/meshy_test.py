"""Meshy Image-to-3D 테스트: 로컬 사진 -> GLB 다운로드

사용법:
  export MESHY_API_KEY="msy_..."
  python3 meshy_test.py dog.jpg [출력폴더]
"""
import base64
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request

BASE = "https://api.meshy.ai/openapi/v1/image-to-3d"
API_KEY = os.environ.get("MESHY_API_KEY")


def request(method, url, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Authorization", f"Bearer {API_KEY}")
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req) as res:
            return json.load(res)
    except urllib.error.HTTPError as e:
        # 401: 키 오류 / 402: 크레딧 부족 또는 무료 플랜 / 429: 요청 과다
        sys.exit(f"HTTP {e.code}: {e.read().decode()}")


def to_data_uri(path):
    mime = mimetypes.guess_type(path)[0] or "image/jpeg"
    with open(path, "rb") as f:
        return f"data:{mime};base64,{base64.b64encode(f.read()).decode()}"


def create_task(image_path):
    body = {
        "image_url": to_data_uri(image_path),
        "ai_model": "latest",
        "should_texture": True,
        "should_remesh": True,         # 리깅용으로 폴리곤 정리
        "topology": "triangle",
        "target_polycount": 30000,     # 자동 웨이트 계산이 실패하지 않을 정도로
        "target_formats": ["glb"],
        "multi_view_thumbnails": True,  # 앞/옆/뒤 미리보기 (사용자 확인용)
    }
    return request("POST", BASE, body)["result"]


def wait(task_id):
    while True:
        task = request("GET", f"{BASE}/{task_id}")
        print(f"  {task['status']} {task.get('progress', 0)}%")
        if task["status"] == "SUCCEEDED":
            return task
        if task["status"] in ("FAILED", "CANCELED"):
            sys.exit(f"실패: {task.get('task_error')}")
        time.sleep(5)


def main():
    if not API_KEY:
        sys.exit("MESHY_API_KEY 환경변수를 설정하세요.")
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    image_path = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else "."
    os.makedirs(out_dir, exist_ok=True)

    task_id = create_task(image_path)
    print(f"작업 생성: {task_id}")
    task = wait(task_id)

    out = os.path.join(out_dir, f"{task_id}.glb")
    urllib.request.urlretrieve(task["model_urls"]["glb"], out)
    print(f"완료: {out} (사용 크레딧 {task.get('consumed_credits')})")
    print("미리보기:", json.dumps(task.get("thumbnail_urls"), indent=2))


if __name__ == "__main__":
    main()
