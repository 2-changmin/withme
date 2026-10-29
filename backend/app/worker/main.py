"""작업자 진입점: `python -m app.worker.main`

큐에서 작업을 하나씩 가져와 handlers에 등록된 처리 함수로 실행한다.
"""
import logging
import signal
import time
import traceback

from app import pipeline, queue
from app.config import get_settings
from app.errors import AppError, ErrorCode
from app.worker.handlers import HANDLERS, JobContext

log = logging.getLogger("worker")
_running = True


def _stop(*_):
    global _running
    _running = False
    log.info("종료 신호를 받았습니다. 현재 작업을 마치고 멈춥니다.")


class LeaseLost(Exception):
    """lease가 끝나 다른 작업자가 작업을 가져갔다. 처리를 멈추고 결과도 쓰지 않는다."""


def run_once() -> bool:
    """작업 하나를 처리했으면 True."""
    s = get_settings()
    job = queue.claim(s.worker_id, list(HANDLERS), s.worker_lease_seconds)
    if job is None:
        return False

    job_id = job["id"]

    def heartbeat(percent: int) -> None:
        if not queue.progress(job_id, s.worker_id, percent, s.worker_lease_seconds):
            raise LeaseLost

    def fail(code: str, message: str, retry: bool) -> None:
        if not queue.fail(job_id, s.worker_id, code, message, retry):
            log.warning("이미 다른 작업자가 가져간 작업이라 실패 기록을 버립니다 %s", job_id)

    log.info("시작 %s %s (시도 %d)", job["type"], job_id, job["attempts"])
    ctx = JobContext(job, heartbeat)
    try:
        result = HANDLERS[job["type"]](ctx)
        if queue.complete(job_id, s.worker_id, result or {}):
            log.info("완료 %s", job_id)
            _after(job, result or {})
        else:
            log.warning("이미 다른 작업자가 가져간 작업이라 결과를 버립니다 %s", job_id)
    except LeaseLost:
        log.warning("lease를 잃어 처리를 멈춥니다 %s", job_id)
    except NotImplementedError as e:
        fail(ErrorCode.NOT_IMPLEMENTED, str(e) or "구현 전", retry=False)
        log.warning("구현 전 %s %s", job["type"], job_id)
    except AppError as e:
        fail(e.code, e.message, retry=e.code == ErrorCode.UPSTREAM_FAILED)
        log.warning("실패 %s %s: %s", job_id, e.code, e.message)
    except Exception as e:  # 예상 못 한 오류는 재시도
        fail(ErrorCode.INTERNAL, f"{e}\n{traceback.format_exc()}", retry=True)
        log.exception("오류 %s", job_id)
    return True


def _after(job: dict, result: dict) -> None:
    """결과를 캐릭터 테이블로 옮기고 다음 단계를 등록한다. 실패해도 작업 결과는 그대로 둔다."""
    try:
        pipeline.on_succeeded(job, result)
    except Exception:
        log.exception("후처리 실패 %s", job["id"])


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
