"""glTF export: mesh + armature, one glTF animation per NLA track.

Channel policy in the written GLB:
- rotation for every joint, in every clip (a held pose such as SitIdle needs
  its constant leg rotations; dropping them would snap back to standing)
- translation only for bones listed in TRANSLATION_BONES (pelvis, used by the
  ground clips with values computed for this model)
- no scale
So clips never override this model's bone lengths.
"""
import json
import struct

import bpy

TRANSLATION_BONES = {"pelvis"}


def export_glb(mesh, arm, path):
    bpy.context.scene.frame_set(1)
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_skins=True,
        export_animations=True,
        export_animation_mode="NLA_TRACKS",
        export_force_sampling=False,
        # The optimizer drops constant channels; filtering is done below instead.
        export_optimize_animation_size=False,
        export_def_bones=False,
    )
    return strip_channels(path)


def _read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    json_len = struct.unpack_from("<I", data, 12)[0]
    gltf = json.loads(data[20:20 + json_len])
    rest = data[20 + json_len:]  # BIN chunk (header included)
    return gltf, rest


def _write_glb(path, gltf, rest):
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * (-len(js) % 4)
    total = 12 + 8 + len(js) + len(rest)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(js), b"JSON"))
        f.write(js)
        f.write(rest)


def strip_channels(path):
    gltf, rest = _read_glb(path)
    nodes = gltf.get("nodes", [])
    removed = 0
    for anim in gltf.get("animations", []):
        keep = []
        for c in anim["channels"]:
            p, name = c["target"]["path"], nodes[c["target"]["node"]].get("name")
            if p == "rotation" or (p == "translation" and name in TRANSLATION_BONES):
                keep.append(c)
        removed += len(anim["channels"]) - len(keep)
        used = sorted({c["sampler"] for c in keep})
        remap = {old: new for new, old in enumerate(used)}
        anim["samplers"] = [anim["samplers"][i] for i in used]
        for c in keep:
            c["sampler"] = remap[c["sampler"]]
        anim["channels"] = keep
    _write_glb(path, gltf, rest)
    return removed
