"""Local worker boundary; doesn't change backend API/job enums or deployment.

Exit 2 means an inspectable artifact that requires review, not an exception and
not an approved character. Same inputs/job folder reuse a completed result.
"""
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import struct
import subprocess

from dogrig.joint_contract import validate
from dogrig import RigError


class RigExecutionError(RuntimeError):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


@dataclass(frozen=True)
class RigOutcome:
    path: Path
    report: dict
    review_required: bool
    reused: bool


def _sha(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""): result.update(chunk)
    return result.hexdigest()


def _valid_glb(path):
    if not path.exists() or path.stat().st_size < 20: return False
    with path.open("rb") as stream: header = stream.read(12)
    magic, version, size = struct.unpack("<4sII", header)
    return magic == b"glTF" and version == 2 and size == path.stat().st_size


def run_rig(executable, prepared_glb, joints_file, job_directory, timeout=120, runner=subprocess.run):
    source = Path(prepared_glb).resolve(); joints = Path(joints_file).resolve(); directory = Path(job_directory).resolve()
    if not source.is_file() or not joints.is_file(): raise RigExecutionError("INPUT_MISSING")
    try: canonical = validate(json.loads(joints.read_text(encoding="utf-8-sig")))
    except (ValueError, TypeError, OSError, RigError): raise RigExecutionError("JOINTS_INVALID") from None
    if not _valid_glb(source): raise RigExecutionError("INPUT_INVALID")
    directory.mkdir(parents=True, exist_ok=True)
    output = directory / "rigged.glb"; report_file = directory / "report.json"; intent_file = directory / "intent.json"
    if source in (output, report_file, intent_file) or joints in (output, report_file, intent_file): raise RigExecutionError("OUTPUT_OVERWRITES_INPUT")
    intent = {"inputSha256": _sha(source), "jointsSha256": hashlib.sha256(json.dumps(canonical, sort_keys=True).encode()).hexdigest(), "version": 1}
    reused = False
    if intent_file.exists():
        try: previous = json.loads(intent_file.read_text(encoding="utf-8"))
        except ValueError: raise RigExecutionError("CHECKPOINT_INVALID") from None
        if previous != intent: raise RigExecutionError("JOB_CONFLICT")
        if report_file.exists() and _valid_glb(output):
            try: report = json.loads(report_file.read_text(encoding="utf-8"))
            except ValueError: raise RigExecutionError("REPORT_INVALID") from None
            if report.get("status") in ("ok", "needs_review"): reused = True
    elif output.exists() or report_file.exists():
        raise RigExecutionError("JOB_CONFLICT")
    if not reused:
        temporary = intent_file.with_suffix(".tmp"); temporary.write_text(json.dumps(intent), encoding="utf-8"); temporary.replace(intent_file)
        script = Path(__file__).resolve().parent / "rig.py"
        command = [str(executable), "-b", "--factory-startup", "-P", str(script), "--", str(source), str(joints), str(output), "--report", str(report_file)]
        try: process = runner(command, shell=False, check=False, capture_output=True, text=True, timeout=timeout)
        except subprocess.TimeoutExpired: raise RigExecutionError("TIMEOUT") from None
        except OSError: raise RigExecutionError("BLENDER_UNAVAILABLE") from None
        if not report_file.exists(): raise RigExecutionError("REPORT_MISSING")
        try: report = json.loads(report_file.read_text(encoding="utf-8"))
        except ValueError: raise RigExecutionError("REPORT_INVALID") from None
        expected = {0: "ok", 2: "needs_review"}.get(process.returncode)
        if expected is None or report.get("status") != expected: raise RigExecutionError("BLENDER_FAILED")
    if not _valid_glb(output): raise RigExecutionError("OUTPUT_INVALID")
    if report.get("skeletonVersion") != 1 or report.get("jointsFormat") != "git-normalized-v1": raise RigExecutionError("REPORT_INVALID")
    quality = report.get("quality")
    if not isinstance(quality, dict) or type(quality.get("accepted")) is not bool: raise RigExecutionError("REPORT_INVALID")
    if report["status"] == "needs_review" and quality["accepted"]: raise RigExecutionError("REPORT_INVALID")
    return RigOutcome(output, report, report["status"] == "needs_review" or quality.get("requires_visual_review", True), reused)
