import copy
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import types
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from worker_adapter import run_rig, RigExecutionError
import test_joint_contract as contract_tests


def glb(path):
    path.write_bytes(struct.pack("<4sII", b"glTF", 2, 24) + struct.pack("<I4s", 4, b"JSON") + b"{}  ")


class WorkerAdapterTests(unittest.TestCase):
    def setUp(self):
        base = (Path(__file__).resolve().parents[2] / "unity/withme_frontend/Temp/batch/tests").resolve(); base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=base, prefix="adapter-")
        assert Path(self.temp.name).resolve().is_relative_to(base)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.source = self.root / "source.glb"; glb(self.source)
        contract_tests.JointContractTests.setUpClass(); self.joints = self.root / "joints.json"; self.joints.write_text(json.dumps(contract_tests.JointContractTests.example))
        self.calls = 0

    def process(self, code=2, status="needs_review", accepted=False):
        def runner(command, **options):
            self.calls += 1; self.assertFalse(options["shell"])
            self.assertEqual(options["timeout"], 120)
            output = Path(command[command.index("--") + 3]); report = Path(command[-1]); glb(output)
            report.write_text(json.dumps({"status": status, "skeletonVersion": 1, "jointsFormat": "git-normalized-v1", "quality": {"accepted": accepted, "requires_visual_review": True}}))
            return types.SimpleNamespace(returncode=code)
        return runner

    def run_job(self, runner=None):
        return run_rig(sys.executable, self.source, self.joints, self.root / "job", runner=runner or self.process())

    def test_exit_two_is_review_not_approval(self):
        result = self.run_job(); self.assertTrue(result.review_required); self.assertFalse(result.reused)
        self.assertFalse(result.report["quality"]["accepted"])

    def test_identical_job_reuses_without_reexecution(self):
        self.run_job(); result = self.run_job(); self.assertTrue(result.reused); self.assertEqual(self.calls, 1)

    def test_changed_input_cannot_overwrite_job(self):
        self.run_job(); data = json.loads(self.joints.read_text()); data["joints"]["nose"][1] += .01; self.joints.write_text(json.dumps(data))
        with self.assertRaisesRegex(RigExecutionError, "JOB_CONFLICT"): self.run_job()
        self.assertEqual(self.calls, 1)

    def test_successful_generation_still_requires_visual_review(self):
        self.assertTrue(self.run_job(self.process(0, "ok", True)).review_required)

    def test_timeout_does_not_retry(self):
        def timeout(*args, **kwargs): self.calls += 1; raise subprocess.TimeoutExpired("blender", 120)
        with self.assertRaisesRegex(RigExecutionError, "TIMEOUT"): self.run_job(timeout)
        self.assertEqual(self.calls, 1)

    def test_exit_report_mismatch_is_failure(self):
        with self.assertRaisesRegex(RigExecutionError, "BLENDER_FAILED"): self.run_job(self.process(0))

    def test_missing_output_is_failure(self):
        runner = self.process()
        def no_file(*args, **kwargs):
            result = runner(*args, **kwargs); (self.root / "job/rigged.glb").unlink(); return result
        with self.assertRaisesRegex(RigExecutionError, "OUTPUT_INVALID"): self.run_job(no_file)

    def test_invalid_input_never_launches_process(self):
        self.source.write_bytes(b"bad")
        with self.assertRaisesRegex(RigExecutionError, "INPUT_INVALID"): self.run_job()
        self.assertEqual(self.calls, 0)


if __name__ == "__main__": unittest.main()
