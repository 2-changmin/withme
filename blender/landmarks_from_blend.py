"""Run inside Blender (Text Editor > Run Script) after moving the LM_* empties.

Writes landmarks.json next to the .blend file. Hints (tail curve) are dropped
because the edited tail endpoints no longer match them.
"""
import json
import os

import bpy

FORMAT = "dog-landmarks-tmp/0"

points = {o.name[3:]: [round(v, 4) for v in o.location]
          for o in bpy.data.objects if o.name.startswith("LM_")}
out = os.path.join(os.path.dirname(bpy.data.filepath) or os.getcwd(), "landmarks.json")
with open(out, "w", encoding="utf-8") as f:
    json.dump({
        "format": FORMAT,
        "space": "blender: front -Y, up +Z, left +X, height normalized to 1, feet at z=0",
        "source": "manual",
        "points": points,
        "hints": {},
    }, f, ensure_ascii=False, indent=2)
print("wrote", out)
