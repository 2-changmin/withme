"""Prepare a mesh and emit the Git-format automatic joints in the same space.

blender -b --factory-startup -P blender/prepare.py -- IN.glb OUT.glb
        --joints-out OUT.json --report REPORT.json
"""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from dogrig import RigError, prepare, landmarks
from dogrig.joint_contract import to_contract


def run(args):
    prepare.reset_scene()
    mesh = prepare.import_model(args.input)
    cleanup = prepare.clean_mesh(mesh)
    normalize = prepare.normalize(mesh)
    prepare.assert_normalized(mesh)
    mesh["withme_prepared"] = 1
    mesh["withme_space"] = "normalized-v1"
    joints = to_contract(landmarks.estimate(mesh))
    feet = landmarks.ground_components(mesh)
    for dest in (args.output, args.joints_out, args.report):
        if dest:
            Path(dest).resolve().parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True); bpy.context.view_layer.objects.active = mesh
    bpy.ops.export_scene.gltf(filepath=str(Path(args.output).resolve()), export_format="GLB",
                             use_selection=True, export_yup=True, export_skins=False,
                             export_animations=False, export_extras=True)
    Path(args.joints_out).write_text(json.dumps(joints, indent=2) + "\n", encoding="utf-8")
    # Re-import proves that metadata and geometry agree, rather than trusting the tag alone.
    prepare.reset_scene()
    restored = prepare.import_model(args.output)
    if restored.get("withme_prepared") != 1:
        raise RigError("SPACE_MISMATCH", "prepared marker lost in glTF roundtrip")
    prepare.assert_normalized(restored)
    again = prepare.normalize(restored)
    return {"status": "prepared", "blender": bpy.app.version_string,
            "normalize": normalize, "cleanup": cleanup, "roundtrip": again,
            "groundAnalysis": feet,
            "skeletonVersion": 1, "jointsFormat": "git-normalized-v1",
            "quality": {"accepted": False, "requires_visual_review": True}}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("input"); p.add_argument("output")
    p.add_argument("--joints-out", required=True); p.add_argument("--report", required=True)
    args = p.parse_args(argv)
    if Path(args.input).resolve() == Path(args.output).resolve():
        p.error("input/output must be different files")
    try:
        report = run(args); code = 0
    except RigError as e:
        report = {"status": "failed", "error": {"code": e.code, "message": e.message}}; code = 1
    except Exception as e:
        report = {"status": "failed", "error": {"code": "UNEXPECTED", "message": str(e)}}; code = 1
    Path(args.report).resolve().parent.mkdir(parents=True, exist_ok=True)
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("[prepare]", report["status"])
    raise SystemExit(code)


if __name__ == "__main__": main()
