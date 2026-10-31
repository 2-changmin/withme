"""Skin weights via a closed voxel proxy (Meshy meshes are split along UV seams,
which makes Bone Heat fail on the original)."""
import bpy
import numpy as np
from mathutils.geometry import intersect_point_line

from . import RigError

MAX_INFLUENCES = 4

# Floppy ears touch the neck, so the voxel proxy fuses them and Bone Heat
# spreads ear influence into the neck. Ear weights are kept only near the ear.
EAR_RULES = {
    side: {
        "bones": [f"ear_01.{side}", f"ear_02.{side}"],
        "competitors": ["head", "neck_02", "neck_01", "jaw"],
        "radius_scale": 0.6,   # capsule radius = scale * ear length (+ min)
        "radius_min": 0.04,
        "closer_ratio": 0.8,   # must be this much closer to the ear than to competitors
        "z_margin": 0.03,      # no ear weight below the neck/head joint minus this
        "blend": 0.35,         # soft edge width as a fraction of each limit
    }
    for side in ("L", "R")
}


def _select_only(*objs, active):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def _proxy(mesh, voxel_size, target_faces):
    proxy = mesh.copy()
    proxy.data = mesh.data.copy()
    proxy.name = "Dog_proxy"
    bpy.context.scene.collection.objects.link(proxy)
    _select_only(proxy, active=proxy)
    rm = proxy.modifiers.new("remesh", "REMESH")
    rm.mode = "VOXEL"
    rm.voxel_size = voxel_size
    bpy.ops.object.modifier_apply(modifier=rm.name)
    if len(proxy.data.polygons) > target_faces * 1.3:
        dm = proxy.modifiers.new("decimate", "DECIMATE")
        dm.ratio = target_faces / len(proxy.data.polygons)
        bpy.ops.object.modifier_apply(modifier=dm.name)
    return proxy


def _limit_and_normalize(mesh):
    groups = mesh.vertex_groups
    for v in mesh.data.vertices:
        ws = sorted(((g.weight, g.group) for g in v.groups if g.weight > 0), reverse=True)
        keep = ws[:MAX_INFLUENCES]
        total = sum(w for w, _ in keep)
        for w, gi in ws[MAX_INFLUENCES:]:
            groups[gi].remove([v.index])
        if total > 0:
            for w, gi in keep:
                groups[gi].add([v.index], w / total, "REPLACE")


def _segment_distance(p, h, t):
    """Distance from points p (N,3) to segment h-t."""
    d = t - h
    f = np.clip(((p - h) @ d) / max(float(d @ d), 1e-12), 0.0, 1.0)
    return np.linalg.norm(p - (h + f[:, None] * d), axis=1)


def _restrict(mesh, arm, rule):
    bones = arm.data.bones
    seg = {n: (np.array(bones[n].head_local), np.array(bones[n].tail_local))
           for n in rule["bones"] + rule["competitors"]}
    co = np.empty(len(mesh.data.vertices) * 3)
    mesh.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)

    d_own = np.min([_segment_distance(co, *seg[n]) for n in rule["bones"]], axis=0)
    d_comp = np.array([_segment_distance(co, *seg[n]) for n in rule["competitors"]])
    d_other = d_comp.min(axis=0)
    nearest_comp = d_comp.argmin(axis=0)
    length = sum(bones[n].length for n in rule["bones"])
    radius = max(rule["radius_scale"] * length, rule["radius_min"])
    z_floor = bones["neck_02"].tail_local.z - rule["z_margin"]
    limit = rule["closer_ratio"] * d_other

    # Soft mask (1 = keep ear weight, 0 = remove). A hard cut stretches the
    # skin between the moving ear and the still head.
    def ramp(x, width):
        x = np.clip(x / np.maximum(width, 1e-6), 0.0, 1.0)
        return x * x * (3 - 2 * x)
    keep = np.minimum.reduce([
        ramp(radius - d_own, rule["blend"] * radius),
        ramp(limit - d_own, rule["blend"] * limit),
        ramp(co[:, 2] - z_floor, rule["z_margin"]),
    ])

    groups = mesh.vertex_groups
    own = {groups[n].index for n in rule["bones"]}
    competitor_idx = [groups[n].index for n in rule["competitors"]]
    changed = 0
    for v in mesh.data.vertices:
        k = keep[v.index]
        if k >= 0.999:
            continue
        hit = [(g.group, g.weight) for g in v.groups if g.group in own and g.weight > 0]
        if not hit:
            continue
        moved = 0.0
        for gi, w in hit:
            moved += w * (1 - k)
            if k > 0.001:
                groups[gi].add([v.index], w * k, "REPLACE")
            else:
                groups[gi].remove([v.index])
        # Hand the removed share to the nearest competitor (head/neck).
        nearest = competitor_idx[nearest_comp[v.index]]
        current = next((g.weight for g in v.groups if g.group == nearest), 0.0)
        groups[nearest].add([v.index], current + moved, "REPLACE")
        changed += 1
    return changed


def _fill_unweighted(mesh, arm):
    segs = [(b.name, arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
            for b in arm.data.bones if b.use_deform]
    fixed = 0
    for v in mesh.data.vertices:
        if any(g.weight > 0.001 for g in v.groups):
            continue
        p = mesh.matrix_world @ v.co
        best = None
        for name, h, t in segs:
            _, f = intersect_point_line(p, h, t)
            f = min(max(f, 0.0), 1.0)
            d = (p - (h + (t - h) * f)).length
            if best is None or d < best[0]:
                best = (d, name)
        mesh.vertex_groups[best[1]].add([v.index], 1.0, "REPLACE")
        fixed += 1
    return fixed


def _restrict_opposite_leg(mesh, arm):
    """Remove only clearly opposite-side leg leakage, with a soft midline.

    Torso remains a valid influence near joints. No guessed hard anatomical
    segmentation is applied to coat geometry.
    """
    prefixes=('shoulder.','upper_arm.','forearm.','front_paw.',
              'thigh.','shin.','hind_foot.','hind_toe.')
    groups=mesh.vertex_groups
    legs={g.index:g.name for g in groups if g.name.startswith(prefixes)}
    changed=0
    for v in mesh.data.vertices:
        for gi,w in [(g.group,g.weight) for g in v.groups]:
            name=legs.get(gi)
            if not name or w<=0: continue
            sign=1 if arm.data.bones[name].head_local.x>=0 else -1
            if v.co.x*sign>=-0.01: continue
            remove=min(1.0,(-v.co.x*sign-0.01)/0.04)
            if remove>=0.999: groups[gi].remove([v.index])
            else: groups[gi].add([v.index],w*(1-remove),'REPLACE')
            receiver='spine_03' if name.startswith(prefixes[:4]) else 'pelvis'
            target=groups[receiver]
            current=next((g.weight for g in v.groups if g.group==target.index),0)
            target.add([v.index],current+w*remove,'REPLACE'); changed+=1
    return changed


def _smooth_seam_weights(mesh, iterations=4, strength=0.35):
    """Diffuse weights along actual edges, welding UV-split coordinates only.

    No spatial nearest-neighbour smoothing between touching limbs or ears.
    Original mesh topology/UVs are never modified.
    """
    co=np.empty(len(mesh.data.vertices)*3)
    mesh.data.vertices.foreach_get('co',co); co=co.reshape(-1,3)
    _,inverse=np.unique(np.round(co/1e-6).astype(np.int64),axis=0,return_inverse=True)
    n=int(inverse.max())+1; g=len(mesh.vertex_groups)
    values=np.zeros((n,g)); counts=np.bincount(inverse,minlength=n)
    for v in mesh.data.vertices:
        for entry in v.groups: values[inverse[v.index],entry.group]+=entry.weight
    values/=counts[:,None]
    edges=np.empty(len(mesh.data.edges)*2,dtype=np.int32)
    mesh.data.edges.foreach_get('vertices',edges); edges=inverse[edges.reshape(-1,2)]
    edges=np.unique(np.sort(edges,axis=1),axis=0); edges=edges[edges[:,0]!=edges[:,1]]
    a,b=edges.T; degree=np.bincount(np.concatenate((a,b)),minlength=n)
    for _ in range(iterations):
        total=np.zeros_like(values)
        np.add.at(total,a,values[b]); np.add.at(total,b,values[a])
        average=total/np.maximum(degree[:,None],1)
        values=np.where((degree>0)[:,None],values*(1-strength)+average*strength,values)
    for v in mesh.data.vertices:
        row=values[inverse[v.index]]
        for gi in [entry.group for entry in v.groups]: mesh.vertex_groups[gi].remove([v.index])
        keep=np.argsort(row)[-MAX_INFLUENCES:]; norm=row[keep].sum()
        for gi in keep:
            if row[gi]>1e-8: mesh.vertex_groups[int(gi)].add([v.index],float(row[gi]/norm),'REPLACE')
    return {'iterations':iterations,'strength':strength,'welded_positions':n}


def bind(mesh, arm, voxel_size=0.012, target_faces=30000):
    proxy = _proxy(mesh, voxel_size, target_faces)
    proxy_faces = len(proxy.data.polygons)
    _select_only(proxy, arm, active=arm)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    proxy_unweighted = sum(1 for v in proxy.data.vertices if not any(g.weight > 0.001 for g in v.groups))
    if proxy_unweighted > len(proxy.data.vertices) * 0.2:
        raise RigError("BONE_HEAT_FAILED", f"proxy unweighted {proxy_unweighted}/{len(proxy.data.vertices)}")

    for b in arm.data.bones:
        if b.use_deform and b.name not in mesh.vertex_groups:
            mesh.vertex_groups.new(name=b.name)
    dt = mesh.modifiers.new("weights", "DATA_TRANSFER")
    dt.object = proxy
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    _select_only(mesh, active=mesh)
    bpy.ops.object.modifier_apply(modifier=dt.name)
    bpy.data.objects.remove(proxy)

    _limit_and_normalize(mesh)
    smoothing = _smooth_seam_weights(mesh)
    restricted = {name: _restrict(mesh, arm, rule) for name, rule in
                  (("ear.L", EAR_RULES["L"]), ("ear.R", EAR_RULES["R"]))}
    filled = _fill_unweighted(mesh, arm)
    restricted['opposite_leg'] = _restrict_opposite_leg(mesh, arm)
    _limit_and_normalize(mesh)

    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mesh.parent = arm
    mesh.matrix_parent_inverse = arm.matrix_world.inverted()
    return {
        "proxy_faces": proxy_faces,
        "proxy_unweighted": proxy_unweighted,
        "filled_nearest_bone": filled,
        "restricted_verts": restricted,
        "seam_smoothing": smoothing,
    }
