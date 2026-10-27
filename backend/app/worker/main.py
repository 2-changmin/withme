"""작업자 진입점: `python -m app.worker.main`

큐에서 작업을 하나씩 가져와 handlers에 등록된 처리 함수로 실행한다.
"""
import logging
import signal
import time
import traceback

from app import queue
from app.config import get_settings
from app.errors import AppError, ErrorCode
from app.worker.handlers import HANDLERS, JobContext

log = logging.getLogger("worker")
_running = True


def _stop(*_):
    global _running
    _running = False
    log.info("종료 신호를 받았습니다. 현재 작업을 마치고 멈춥니다.")


def run_once() -> bool:
    """작업 하나를 처리했으면 True."""
    s = get_settings()
    job = queue.claim(s.worker_id, list(HANDLERS), s.worker_lease_seconds)
    if job is None:
        return False

    log.info("시작 %s %s (시도 %d)", job["type"], job["id"], job["attempts"])
    ctx = JobContext(job, lambda p: queue.progress(job["id"], p, s.worker_lease_seconds))
    try:
        result = HANDLERS[job["type"]](ctx)
        queue.complete(job["id"], result or {})
        log.info("완료 %s", job["id"])
    except NotImplementedError as e:
        queue.fail(job["id"], ErrorCode.NOT_IMPLEMENTED, str(e) or "구현 전", retry=False)
        log.warning("구현 전 %s %s", job["type"], job["id"])
    except AppError as e:
        queue.fail(job["id"], e.code, e.message, retry=e.code == ErrorCode.UPSTREAM_FAILED)
        log.warning("실패 %s %s: %s", job["id"], e.code, e.message)
    except Exception as e:  # 예상 못 한 오류는 재시도
        queue.fail(job["id"], ErrorCode.INTERNAL, f"{e}\n{traceback.format_exc()}", retry=True)
        log.exception("오류 %s", job["id"])
    return True


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    signal.signal(signal.SIGINT, _stop)
    signal.signal(signal.SIGTERM, _stop)
    s = get_settings()
    log.info("작업자 %s 시작, 처리 종류: %s", s.worker_id, ", ".join(HANDLERS))
    while _running:
        queue.reap_expired()
        if not run_once():
            time.sleep(s.worker_poll_seconds)


if __name__ == "__main__":
    main()
