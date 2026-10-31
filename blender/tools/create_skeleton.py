"""Create the mesh-free v1 rest skeleton from canonical Git landmarks."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import bpy
from dogrig import skeleton, prepare
from dogrig.joint_contract import from_contract

argv = sys.argv[sys.argv.index("--") + 1:]
p = argparse.ArgumentParser(); p.add_argument("joints"); p.add_argument("output"); p.add_argument("--report", required=True)
args = p.parse_args(argv)
prepare.reset_scene()
arm = skeleton.build(from_contract(json.loads(Path(args.joints).read_text(encoding="utf-8-sig"))))
arm["skeletonVersion"] = 1
definitions = [{"name": b.name, "parent": b.parent.name if b.parent else None,
                "deform": b.use_deform, "length": float(b.length),
                "head": list(b.head_local), "tail": list(b.tail_local)} for b in arm.data.bones]
assert len(definitions) == 33 and all(b["length"] >= .01 for b in definitions)
Path(args.output).resolve().parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(Path(args.output).resolve()))
bpy.ops.wm.open_mainfile(filepath=str(Path(args.output).resolve()))
restored = bpy.data.objects["Dog_Rig"]
assert len(restored.data.bones) == 33 and len(bpy.data.objects) == 1
Path(args.report).resolve().parent.mkdir(parents=True, exist_ok=True)
Path(args.report).write_text(json.dumps({"blender": bpy.app.version_string, "skeletonVersion": 1,
    "boneCount": 33, "space": "Blender Z-up, front -Y", "bones": definitions,
    "savedAndReopened": True, "unityCompatibilityTested": False}, indent=2) + "\n", encoding="utf-8")
print("[skeleton] saved and reopened 33-bone template")
