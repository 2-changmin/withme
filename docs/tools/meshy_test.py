"""Meshy Image-to-3D 테스트: 로컬 사진 1~4장 -> GLB 다운로드

사용법:
  export MESHY_API_KEY="msy_..."   (없으면 backend/.env의 MESHY_API_KEY를 읽는다)
  python3 meshy_test.py side.png                                  # 1장: image-to-3d
  python3 meshy_test.py side.png front.png other.png back.png     # 2~4장: multi-image-to-3d
  python3 meshy_test.py side.png -o out --dry-run                 # 요청 내용만 출력 (크레딧 0)

결과는 출력 폴더의 results.jsonl에 한 줄씩 쌓인다 (#47 비교용).
"""
import argparse
import base64
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request

API = "https://api.meshy.ai/openapi/v1"
ENV_FILE = os.path.join(os.path.dirname(__file__), "..", "..", "backend", ".env")


def load_api_key():
    if os.environ.get("MESHY_API_KEY"):
        return os.environ["MESHY_API_KEY"]
    if os.path.exists(ENV_FILE):
        with open(ENV_FILE, encoding="utf-8") as f:
            for line in f:
                key, _, value = line.strip().partition("=")
                if key == "MESHY_API_KEY":
                    return value.strip().strip('"\'')
    return None


API_KEY = load_api_key()


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


def build_request(image_paths):
    """이미지 1장이면 image-to-3d, 2~4장이면 multi-image-to-3d."""
    body = {
        "ai_model": "latest",
        "should_texture": True,
        "should_remesh": True,         # 리깅용으로 폴리곤 정리
        "topology": "triangle",
        "target_polycount": 30000,     # 자동 웨이트 계산이 실패하지 않을 정도로
        "target_formats": ["glb"],
        "multi_view_thumbnails": True,  # 앞/옆/뒤 미리보기 (사용자 확인용)
    }
    if len(image_paths) == 1:
        body["image_url"] = to_data_uri(image_paths[0])
        return f"{API}/image-to-3d", body
    body["image_urls"] = [to_data_uri(p) for p in image_paths]
    return f"{API}/multi-image-to-3d", body


def wait(base, task_id):
    while True:
        task = request("GET", f"{base}/{task_id}")
        print(f"  {task['status']} {task.get('progress', 0)}%")
        if task["status"] == "SUCCEEDED":
            return task
        if task["status"] in ("FAILED", "CANCELED"):
            sys.exit(f"실패: {task.get('task_error')}")
        time.sleep(5)


def summarize(body):
    """data URI는 길어서 앞부분만 보여 준다."""
    shown = dict(body)
    for key in ("image_url", "image_urls"):
        if key in shown:
            uris = shown[key] if isinstance(shown[key], list) else [shown[key]]
            shown[key] = [f"{u[:30]}... ({len(u) // 1024} KB)" for u in uris]
    return json.dumps(shown, indent=2, ensure_ascii=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    parser.add_argument("images", nargs="+", help="입력 이미지 1~4장")
    parser.add_argument("-o", "--out", default=".", help="출력 폴더 (기본: 현재 폴더)")
    parser.add_argument("--dry-run", action="store_true", help="Meshy를 호출하지 않고 요청 내용만 출력")
    args = parser.parse_args()

    if len(args.images) > 4:
        sys.exit("이미지는 최대 4장입니다.")
    for path in args.images:
        if not os.path.isfile(path):
            sys.exit(f"이미지 파일이 없습니다: {path}")
    url, body = build_request(args.images)
    if args.dry_run:
        print(f"POST {url}")
        print(summarize(body))
        return
    if not API_KEY:
        sys.exit("MESHY_API_KEY 환경변수를 설정하세요.")
    os.makedirs(args.out, exist_ok=True)

    started = time.time()
    task_id = request("POST", url, body)["result"]
    print(f"작업 생성: {task_id} ({url.rsplit('/', 1)[-1]}, {len(args.images)}장)")
    task = wait(url, task_id)
    elapsed = time.time() - started

    out = os.path.join(args.out, f"{task_id}.glb")
    urllib.request.urlretrieve(task["model_urls"]["glb"], out)
    print(f"완료: {out} (사용 크레딧 {task.get('consumed_credits')}, 생성 시간 {elapsed:.0f}초)")
    print("미리보기:", json.dumps(task.get("thumbnail_urls"), indent=2))

    with open(os.path.join(args.out, "results.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps({
            "task_id": task_id,
            "endpoint": url.rsplit("/", 1)[-1],
            "images": args.images,
            "consumed_credits": task.get("consumed_credits"),
            "elapsed_seconds": round(elapsed),
            "glb": out,
            "thumbnail_urls": task.get("thumbnail_urls"),
        }, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    main()
