"""Import and normalize a model: one mesh, front -Y, Z up, height 1, feet on Z=0."""
import math

import bpy
import numpy as np
from mathutils import Matrix

from . import RigError


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_coords(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def import_model(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    if not meshes:
        raise RigError("NO_MESH", f"no mesh in {path}")

    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
        o.vertex_groups.clear()
        for m in [m for m in o.modifiers if m.type == "ARMATURE"]:
            o.modifiers.remove(m)
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o)

    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = "Dog"
    return obj


def normalize(obj):
    """Return the applied steps so the transform can be recorded."""
    if obj.get("withme_prepared") == 1:
        assert_normalized(obj)
        return {"steps": [], "already_normalized": True, "scale": 1.0}
    steps = []
    co = mesh_coords(obj)
    ext = co.max(0) - co.min(0)
    # Body should run along Y.
    if ext[0] > ext[1] * 1.15:
        obj.data.transform(Matrix.Rotation(math.pi / 2, 4, "Z"))
        steps.append("rotate_z_90")
        co = mesh_coords(obj)

    # The end with the higher top is the head; it must face -Y.
    y0, y1 = co[:, 1].min(), co[:, 1].max()
    span = y1 - y0
    front_top = co[co[:, 1] < y0 + 0.25 * span, 2].max()
    back_top = co[co[:, 1] > y1 - 0.25 * span, 2].max()
    if back_top > front_top:
        obj.data.transform(Matrix.Rotation(math.pi, 4, "Z"))
        steps.append("rotate_z_180")
        co = mesh_coords(obj)

    mn, mx = co.min(0), co.max(0)
    height = float(mx[2] - mn[2])
    if height <= 0:
        raise RigError("BAD_MESH", "zero height")
    scale = 1.0 / height
    offset = (-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, -mn[2])
    obj.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(offset))
    obj.data.update()
    return {
        "steps": steps,
        "translate_before_scale": [round(float(v), 6) for v in offset],
        "scale": round(scale, 6),
        "original_height": round(height, 6),
    }


def assert_normalized(obj, tolerance=0.001):
    co = mesh_coords(obj)
    if not np.isfinite(co).all() or len(co) < 3:
        raise RigError("BAD_MESH", "non-finite or empty geometry")
    mn, mx = co.min(0), co.max(0)
    if abs(float(mx[2] - mn[2]) - 1) > tolerance or abs(float(mn[2])) > tolerance:
        raise RigError("SPACE_MISMATCH", "prepared geometry must have height 1 and floor z=0")
    if max(abs(float((mn[i] + mx[i]) / 2)) for i in (0, 1)) > tolerance:
        raise RigError("SPACE_MISMATCH", "prepared geometry must be centered in X/Y")
    if obj.get("withme_prepared") == 1 and obj.get("withme_space") != "normalized-v1":
        raise RigError("SPACE_MISMATCH", "prepared metadata is unsupported")


def clean_mesh(obj, max_triangles=20000):
    """Merge exact UV splits (loop UVs remain), then bounded triangle reduction."""
    import bmesh
    before = mesh_health(obj)
    co = mesh_coords(obj)
    height = float(co[:, 2].max() - co[:, 2].min())
    if not np.isfinite(co).all() or height <= 0:
        raise RigError("BAD_MESH", "invalid input geometry")
    bm = bmesh.new()
    try:
        bm.from_mesh(obj.data)
        # Relative tolerance: don't weld nearby but distinct ear/neck or leg surfaces.
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=height * 1e-7)
        bm.to_mesh(obj.data)
    finally:
        bm.free()
    obj.data.update()
    def triangles():
        obj.data.calc_loop_triangles()
        return len(obj.data.loop_triangles)
    for _ in range(2):
        count = triangles()
        if count <= max_triangles:
            break
        modifier = obj.modifiers.new("WithmeTriangleBudget", "DECIMATE")
        modifier.ratio = min(1.0, (max_triangles - 100) / count)
        modifier.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    if triangles() > max_triangles:
        raise RigError("MESH_BUDGET", "triangle budget not reached within two passes")
    return {"before": before, "after": mesh_health(obj), "triangles": triangles(),
            "requires_visual_review": True, "merge_relative_tolerance": 1e-7}


def mesh_health(obj):
    import bmesh

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    non_manifold = sum(1 for e in bm.edges if not e.is_manifold)
    bm.free()
    return {
        "verts": len(obj.data.vertices),
        "faces": len(obj.data.polygons),
        "non_manifold_edges": non_manifold,
    }
