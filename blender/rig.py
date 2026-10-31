"""Public CLI: IN.glb JOINTS.json OUT.glb. The JSON is the Git API contract.

Exit 0 = generated with no automatic issues, 2 = generated but needs visual
review, 1 = failure. Never translate exit 2 into an approved product result.
"""
import argparse
import json
from pathlib import Path
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parent))
from dogrig import RigError
from dogrig.joint_contract import from_contract


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("input"); p.add_argument("joints"); p.add_argument("output")
    p.add_argument("--report"); p.add_argument("--blend")
    args = p.parse_args(argv)
    report_path = Path(args.report or args.output + ".report.json")
    report_path.resolve().parent.mkdir(parents=True, exist_ok=True)
    if Path(args.output).resolve() in {Path(args.input).resolve(), Path(args.joints).resolve()}:
        p.error("output must not overwrite input")
    try:
        private = from_contract(json.loads(Path(args.joints).read_text(encoding="utf-8-sig")))
        # Only import Blender pipeline after cheap boundary validation has succeeded.
        import source_rig
        with tempfile.TemporaryDirectory(prefix="withme-joints-", dir=report_path.resolve().parent) as directory:
            assert Path(directory).resolve().is_relative_to(report_path.resolve().parent)
            path = Path(directory) / "internal.json"
            path.write_text(json.dumps(private), encoding="utf-8")
            args.landmarks = str(path); args.landmarks_out = None; args.report = str(report_path)
            report = source_rig.run(args)
            report["jointsFormat"] = "git-normalized-v1"
        code = 0 if report["status"] == "ok" else 2
    except RigError as e:
        report = {"status": "failed", "error": {"code": e.code, "message": e.message}}; code = 1
    except Exception as e:
        report = {"status": "failed", "error": {"code": "UNEXPECTED", "message": str(e)}}; code = 1
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("[rig]", report["status"])
    raise SystemExit(code)


if __name__ == "__main__": main()
