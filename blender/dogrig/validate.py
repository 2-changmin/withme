"""Automatic checks. Thresholds are in normalized units (model height = 1)."""
import bpy
import numpy as np

from . import animations

GROUND_TOLERANCE = 0.015   # penetration allowed for the lowest vertex
FLOAT_TOLERANCE = 0.02     # all feet off the ground (trot has a short suspension)
# Edge length change. Long fur strands on tails/legs stretch a lot even in
# acceptable motion, so this is a coarse alarm; judge by eye in Blender.
STRETCH_TOLERANCE = 0.6
STRETCH_PERCENTILE = 99.0
BONE_MARGIN = 0.05         # bones may sit this far outside the mesh bbox


def _eval_coords(mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    m = ev.to_mesh()
    co = np.empty(len(m.vertices) * 3)
    m.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    W = np.array(mesh.matrix_world)
    return co @ W[:3, :3].T + W[:3, 3]


def static_checks(mesh, arm):
    issues = []
    unweighted = sum(1 for v in mesh.data.vertices if not any(g.weight > 0.001 for g in v.groups))
    if unweighted:
        issues.append({"code": "UNWEIGHTED_VERTS", "count": unweighted})

    co = _eval_coords(mesh)
    lo, hi = co.min(0) - BONE_MARGIN, co.max(0) + BONE_MARGIN
    outside = []
    for b in arm.data.bones:
        for p in (b.head_local, b.tail_local):
            if np.any(np.array(p) < lo) or np.any(np.array(p) > hi):
                outside.append(b.name)
                break
    if outside:
        issues.append({"code": "BONE_OUTSIDE_MESH", "bones": outside})

    used = {g.group for v in mesh.data.vertices for g in v.groups if g.weight > 0.001}
    empty_groups = [g.name for g in mesh.vertex_groups if g.index not in used]
    return {"unweighted_verts": unweighted, "empty_bone_groups": empty_groups, "issues": issues}


def clip_checks(mesh, arm, action, samples=None):
    # A held pose may already be distorted at frame 1: use bind geometry.
    ad = arm.animation_data
    ad.action = None
    for tr in ad.nla_tracks:
        tr.mute = True
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    sc = bpy.context.scene
    sc.frame_set(1)
    # Stretch = edge length change. A bbox check would flag legitimate motion
    # such as a tail swinging sideways.
    ev = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
    mesh.data.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)
    rest = _eval_coords(mesh)
    rest_len = np.linalg.norm(rest[ev[:, 0]] - rest[ev[:, 1]], axis=1)
    ok = rest_len > 1e-5
    animations.activate(arm, action)
    n = int(action.frame_range[1])
    frames = list(range(1, n + 1)) if samples is None else sorted({1 + round(i * (n - 1) / (samples - 1)) for i in range(samples)})
    from .contact import masks
    foot_masks = masks(mesh)
    foot_ranges = {k: [1e9, -1e9] for k in foot_masks if k != 'body'}
    first = None
    max_step = 0.0
    previous = None
    min_z, max_lowest, worst_stretch = 1e9, -1e9, 0.0
    for f in frames:
        sc.frame_set(f)
        co = _eval_coords(mesh)
        if not np.isfinite(co).all():
            raise ValueError('Non-finite mesh coordinates: ' + action.name)
        if first is None:
            first = co.copy()
        if previous is not None:
            max_step = max(max_step, float(np.linalg.norm(co - previous, axis=1).max()))
        previous = co.copy()
        for key, bounds in foot_ranges.items():
            if foot_masks[key].any():
                low = float(co[foot_masks[key], 2].min())
                bounds[0] = min(bounds[0], low)
                bounds[1] = max(bounds[1], low)
        z = float(co[:, 2].min())
        min_z, max_lowest = min(min_z, z), max(max_lowest, z)
        length = np.linalg.norm(co[ev[:, 0]] - co[ev[:, 1]], axis=1)
        change = np.abs(length[ok] / rest_len[ok] - 1)
        worst_stretch = max(worst_stretch, float(np.percentile(change, STRETCH_PERCENTILE)))
    animations.deactivate(arm)
    sc.frame_set(1)

    issues = []
    if min_z < -GROUND_TOLERANCE:
        issues.append({"code": "GROUND_PENETRATION", "min_z": round(min_z, 4)})
    if max_lowest > FLOAT_TOLERANCE:
        issues.append({"code": "FLOATING", "lowest_z_max": round(max_lowest, 4)})
    if worst_stretch > STRETCH_TOLERANCE:
        issues.append({"code": "STRETCH", "ratio": round(worst_stretch, 3)})
    return {
        "frames": n,
        "lowest_z_range": [round(min_z, 4), round(max_lowest, 4)],
        "edge_stretch_p99": round(worst_stretch, 3),
        "stretch_reference": "bind_pose",
        "sampled_frames": len(frames),
        "foot_z_ranges": {k: [round(v, 5) for v in b] for k, b in foot_ranges.items()},
        "endpoint_max_delta": round(float(np.linalg.norm(co-first,axis=1).max()), 6),
        "max_frame_vertex_step": round(max_step, 6),
        "issues": issues,
    }
