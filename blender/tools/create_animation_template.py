"""Keep the standard rig/actions, without the validation dog's mesh."""
import argparse
import json
from pathlib import Path
import sys

import bpy

p = argparse.ArgumentParser(); p.add_argument("source"); p.add_argument("output"); p.add_argument("--report", required=True)
args = p.parse_args(sys.argv[sys.argv.index("--") + 1:])
bpy.ops.wm.open_mainfile(filepath=str(Path(args.source).resolve()))
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for obj in list(bpy.data.objects):
    if obj != arm: bpy.data.objects.remove(obj, do_unlink=True)
assert len(arm.data.bones) == 33 and len(bpy.data.actions) == 17
arm["requires_model_pelvis_retarget"] = True
arm["skeletonVersion"] = 1
clips = []
for action in bpy.data.actions:
    forbidden = [f.data_path for f in action.fcurves if ".scale" in f.data_path or (".location" in f.data_path and 'pose.bones["pelvis"]' not in f.data_path)]
    assert not forbidden, forbidden
    action.use_fake_user = True
    clips.append({"name": action.name, "frameRange": list(action.frame_range)})
bpy.ops.wm.save_as_mainfile(filepath=str(Path(args.output).resolve()))
Path(args.report).write_text(json.dumps({"skeletonVersion": 1, "blender": bpy.app.version_string,
    "boneCount": 33, "clipCount": 17, "clips": clips, "meshFree": True,
    "modelSpecificPelvisRetargetRequired": True, "qualityApproved": False}, indent=2) + "\n", encoding="utf-8")
print("[animations] saved 17-clip mesh-free template")
