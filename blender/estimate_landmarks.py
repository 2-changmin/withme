"""Write estimated landmarks for hand editing.

blender -b --factory-startup -P blender/estimate_landmarks.py -- IN.glb OUT.json [--blend OUT.blend]

Edit the JSON (or move the LM_* empties in the saved .blend and run
landmarks_from_blend.py inside Blender), then pass it to rig.py --landmarks.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from dogrig import landmarks, prepare  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
p = argparse.ArgumentParser(prog="estimate_landmarks.py")
p.add_argument("input")
p.add_argument("output")
p.add_argument("--blend")
args = p.parse_args(argv)

prepare.reset_scene()
mesh = prepare.import_model(args.input)
norm = prepare.normalize(mesh)
lm = landmarks.estimate(mesh)
landmarks.save(lm, args.output, meta={"model": os.path.basename(args.input), "normalize": norm})
if args.blend:
    col = bpy.data.collections.new("Landmarks")
    bpy.context.scene.collection.children.link(col)
    for name, pt in lm["points"].items():
        e = bpy.data.objects.new(f"LM_{name}", None)
        e.empty_display_type = "SPHERE"
        e.empty_display_size = 0.02
        e.location = pt
        col.objects.link(e)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.blend))
print(f"[landmarks] wrote {args.output}")
