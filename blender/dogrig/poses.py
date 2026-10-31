"""Ground-contact clips (sit, lie down) baked per model with a temporary IK rig.

The pelvis moves down (its location is keyed, computed from this model's leg
lengths), while IK keeps paws, hocks and toes on target points on the ground.
After solving every frame, the result is stored as plain bone rotations (plus
pelvis location) and the IK rig is removed, so the export has no constraints.

Pose parameters are per side-agnostic fractions of hip height (hh) and
shoulder height (hs), all in normalized units.
"""
import json
import math
import os

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

from .animations import write_clip

R = math.radians
SIDES = ("L", "R")


def _smooth(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


def _lerp(a, b, s):
    return a + (b - a) * s


# ---------------------------------------------------------------- rest data

def _context(arm, mesh):
    B = arm.data.bones
    hh = B["thigh.L"].head_local.z
    hs = B["upper_arm.L"].head_local.z
    co = np.empty(len(mesh.data.vertices) * 3)
    mesh.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    y0 = B["upper_arm.L"].head_local.y + 0.05
    y1 = B["thigh.L"].head_local.y - 0.05
    trunk = co[(np.abs(co[:, 0]) < 0.1) & (co[:, 1] > y0) & (co[:, 1] < y1)]
    ctx = {
        "hh": hh,
        "hs": hs,
        "belly": float(trunk[:, 2].min()) if len(trunk) else 0.4 * hh,
        "pelvis": B["pelvis"].head_local.copy(),
        "chest": B["spine_03"].tail_local.copy(),
        "scapula": Vector((0, B["shoulder.L"].head_local.y, B["shoulder.L"].head_local.z)),
        "spine_pts": [B["spine_01"].head_local.copy(), B["spine_01"].tail_local.copy(),
                      B["spine_02"].tail_local.copy(), B["spine_03"].tail_local.copy()],
        "thigh": B["thigh.L"].length,
    }
    for s in SIDES:
        ctx[s] = {
            "wrist": B[f"forearm.{s}"].tail_local.copy(),
            "ftoe": B[f"front_paw.{s}"].tail_local.copy(),
            "hock": B[f"shin.{s}"].tail_local.copy(),
            "ball": B[f"hind_foot.{s}"].tail_local.copy(),
            "htoe": B[f"hind_toe.{s}"].tail_local.copy(),
            "hip": B[f"thigh.{s}"].head_local.copy(),
            "arm_len": B[f"upper_arm.{s}"].length + B[f"forearm.{s}"].length,
            "upper_len": B[f"upper_arm.{s}"].length,
            "thigh_len": B[f"thigh.{s}"].length,
            "shin_len": B[f"shin.{s}"].length,
            "fore_len": B[f"forearm.{s}"].length,
            "joint": B[f"shoulder.{s}"].tail_local.copy(),
            "scap_head": B[f"shoulder.{s}"].head_local.copy(),
            "scap_vec": B[f"shoulder.{s}"].tail_local - B[f"shoulder.{s}"].head_local,
        }
        ctx[s]["scap_fit"] = _fit_scapula(B[f"shoulder.{s}"], ctx[s])
    return ctx


SCAPULA_TILT_MAX = 30.0  # degrees the shoulder blade may tilt when sitting
LIE_ELBOW_Z = 0.05       # elbow (bone line) height when lying: about the forearm's thickness
LIE_KNEE_Z = 0.05        # knee/hock (bone line) height when lying: on the ground
LIE_SCAPULA_TILT_MAX = 45.0  # shoulder blade tilt allowed to set the shoulder height when lying
LIE_TAIL = {"tail_01": (38, 0, 0), "tail_02": (12, 0, 0), "tail_03": (6, 0, 0)}


def _fit_scapula(bone, c):
    """Tilt (degrees, +X) of the shoulder blade that brings the shoulder joint
    closest to one straight arm length above the standing wrist height.
    Returns (tilt, joint, dz): dz is the height still missing, made up by
    raising the body."""
    head = bone.head_local
    d = bone.tail_local - head
    target = c["wrist"].z + c["arm_len"]
    best = None
    steps = int(SCAPULA_TILT_MAX * 2)
    for i in range(-steps, steps + 1):
        a = i * 0.5
        joint = head + Matrix.Rotation(R(a), 3, "X") @ d
        err = abs(joint.z - target)
        if best is None or err < best[0]:
            best = (err, a, joint)
    return best[1], best[2], target - best[2].z


def _standing(ctx):
    """Target dict for the rest pose."""
    p = {"pelvis_loc": Vector(), "pelvis_rot": 0.0, "straight": 0.0, "hind_aim": 0.0, "fk": {}}
    for s in SIDES:
        for k in ("wrist", "ftoe", "hock", "ball", "htoe"):
            p[f"{k}.{s}"] = ctx[s][k].copy()
        p[f"aim_u.{s}"] = p[f"aim_f.{s}"] = ctx[s]["wrist"] - Vector((0, 0, 5))
        p[f"aim_t.{s}"] = p[f"aim_s.{s}"] = ctx[s]["hock"] - Vector((0, 0, 5))
    return p




def _pitch(theta):
    """Rotation that raises the front of the body by theta degrees."""
    return Matrix.Rotation(R(-theta), 3, "X")


def _sit(ctx, theta=None):
    """Front legs stay straight with the chest pinned over the planted front
    paws; the body pivots up around the chest until the rump touches the
    ground (theta solved per model in _solve_sit). Hips therefore slide
    forward, and the hind feet come forward under them."""
    hh = ctx["hh"]
    theta = ctx.get("sit_theta", 30.0) if theta is None else theta
    p = _standing(ctx)
    P, chest = ctx["pelvis"], ctx["chest"]
    # The pelvis tilts by theta and the spine bends up a further SIT_SPINE
    # degrees (split over its three bones), so the trunk stands more upright
    # than the rump alone would allow.
    step = SIT_SPINE / 3
    pts = ctx["spine_pts"]  # spine_01 head .. spine_03 tail
    scap = ctx["scapula"]   # shoulder blade top, a child point of spine_03
    reach = (_pitch(theta + step) @ (pts[1] - pts[0])
             + _pitch(theta + 2 * step) @ (pts[2] - pts[1])
             + _pitch(theta + 3 * step) @ (scap - pts[2]))
    rot = _pitch(theta)
    # Pin the shoulder blade where it is when standing; with the blade's own
    # rotation undone below, the front legs keep their standing shape.
    p["pelvis_loc"] = (scap - P) - rot @ (pts[0] - P) - reach
    p["pelvis_loc"].x = 0.0
    raise_z = max(0.0, max(ctx[s]["scap_fit"][2] for s in SIDES))
    p["pelvis_loc"].z += raise_z
    p["pelvis_rot"] = -theta
    upright = theta + SIT_SPINE
    for s in SIDES:
        c = ctx[s]
        hip = P + p["pelvis_loc"] + rot @ (c["hip"] - P)
        meta = (c["hock"] - c["ball"]).length
        toe = (c["htoe"] - c["ball"]).length
        # Hock flat on the ground just under/behind the hip, foot pointing forward.
        hock = Vector((c["hock"].x * 1.1, hip.y - 0.1 * hh, 0.07 * hh))
        p[f"hock.{s}"] = hock
        p[f"ball.{s}"] = hock + Vector((0, -meta, -0.6 * hock.z + 0.03))
        p[f"htoe.{s}"] = p[f"ball.{s}"] + Vector((0, -toe, -0.01))
    # Head and neck undo most of the pitch so the dog looks forward; the tail
    # rests back along the ground.
    p["fk"] = {"neck_01": (0.35 * upright, 0, 0), "neck_02": (0.3 * upright, 0, 0), "head": (0.25 * upright, 0, 0),
               "tail_01": (theta + 45 + ctx.get('sit_tail_adjust',0), 0, 0), "tail_02": (ctx.get('sit_tail_02',30), 0, 0), "tail_03": (ctx.get('sit_tail_03',20), 0, 0)}
    for k in range(3):
        p["fk"][f"spine_0{k + 1}"] = (-step, 0, 0)  # front up
    for s in SIDES:
        # Front legs: upper arm and forearm in one straight vertical line.
        # The shoulder blade (first segment from the body) takes up the
        # length difference by tilting a little.
        c = ctx[s]
        tilt, joint, _ = c["scap_fit"]
        p["fk"][f"shoulder.{s}"] = (upright + tilt, 0, 0)
        wrist = Vector((joint.x, joint.y, joint.z + raise_z - c["arm_len"]))
        p[f"wrist.{s}"] = wrist
        p[f"aim_u.{s}"] = p[f"aim_f.{s}"] = wrist - Vector((0, 0, 5))
        p[f"ftoe.{s}"] = wrist + (c["ftoe"] - c["wrist"])
    p["straight"] = 1.0
    return p


def _lie(ctx):
    hh, hs = ctx["hh"], ctx["hs"]
    p = _standing(ctx)
    p["pelvis_loc"] = Vector((0, 0, -(ctx["belly"] - 0.02) + ctx.get("lift_lie", 0.0)))
    p["pelvis_rot"] = -3.0
    rot = _pitch(3.0)
    P = ctx["pelvis"]
    for s in SIDES:
        c = ctx[s]
        paw = (c["ftoe"] - c["wrist"]).length
        # Front legs in an "L": upper arm straight down from the shoulder joint
        # to the elbow on the ground, forearm flat forward from the elbow, paw
        # in front. The shoulder blade tilts so the joint sits one upper-arm
        # length above the ground.
        head = P + p["pelvis_loc"] + rot @ (c["scap_head"] - P)
        d = rot @ c["scap_vec"]
        target_z = LIE_ELBOW_Z + c["upper_len"]
        tilt, joint = min(
            ((a * 0.5, head + Matrix.Rotation(R(a * 0.5), 3, "X") @ d)
             for a in range(-int(LIE_SCAPULA_TILT_MAX * 2), int(LIE_SCAPULA_TILT_MAX * 2) + 1)),
            key=lambda tj: abs(tj[1].z - target_z))
        p["fk"][f"shoulder.{s}"] = (tilt, 0, 0)
        drop = joint.z - LIE_ELBOW_Z
        if drop >= c["upper_len"]:
            # Joint still too high: arm hangs straight down.
            elbow = joint - Vector((0, 0, c["upper_len"]))
        else:
            # Joint too low: the upper arm leans forward, never back, so the
            # forearm never crosses under it.
            elbow = Vector((joint.x, joint.y - math.sqrt(c["upper_len"] ** 2 - drop ** 2), LIE_ELBOW_Z))
        wrist = elbow + Vector((0, -c["fore_len"], 0))
        p[f"aim_u.{s}"] = joint + (elbow - joint) * 10
        p[f"aim_f.{s}"] = elbow + Vector((0, -5, 0))
        p[f"wrist.{s}"] = wrist
        p[f"ftoe.{s}"] = wrist + Vector((0, -paw, -0.01))
        meta = (c["hock"] - c["ball"]).length
        toe = (c["htoe"] - c["ball"]).length
        # Hind legs: hock (heel) on the ground behind the hip, knee folded
        # forward beside the belly, foot flat pointing forward.
        hip = P + p["pelvis_loc"] + rot @ (c["hip"] - P)
        lt, ls = c["thigh_len"], c["shin_len"]
        # Thigh goes down and back so the knee rests on the ground behind the
        # hip (in the leg's own side plane, no splaying). From the knee the
        # shin, then the foot, lie forward along the ground.
        drop = max(hip.z - LIE_KNEE_Z, 0.0)
        back = math.sqrt(max(lt ** 2 - drop ** 2, 0.0))
        knee = Vector((hip.x, hip.y + back, hip.z - min(drop, lt)))
        hock = Vector((knee.x, knee.y - ls, LIE_KNEE_Z))
        p[f"aim_t.{s}"] = hip + (knee - hip) * 10
        p[f"aim_s.{s}"] = knee + (hock - knee) * 10
        p[f"hock.{s}"] = hock
        p[f"ball.{s}"] = hock + Vector((0, -meta, -0.015))
        p[f"htoe.{s}"] = p[f"ball.{s}"] + Vector((0, -toe, 0))
    p["straight"] = 1.0
    p["hind_aim"] = 1.0
    # Tail swings back to rest along the ground instead of through it.
    p["fk"].update(LIE_TAIL)
    return p


def _sleep(ctx):
    p = _lie(ctx)
    p["fk"].update({"neck_01": (25, 0, 0), "neck_02": (15, 0, 0), "head": (15, 0, 0)})
    p["fk"]["tail_02"] = (20, 0, 25)  # curled to one side
    return p


def _mix(a, b, s):
    out = {"pelvis_loc": a["pelvis_loc"].lerp(b["pelvis_loc"], s),
           "pelvis_rot": _lerp(a["pelvis_rot"], b["pelvis_rot"], s),
           "straight": _lerp(a["straight"], b["straight"], s),
           "hind_aim": _lerp(a["hind_aim"], b["hind_aim"], s), "fk": {}}
    for k in a:
        if k not in ("pelvis_loc", "pelvis_rot", "straight", "hind_aim", "fk"):
            out[k] = a[k].lerp(b[k], s)
    for bone in set(a["fk"]) | set(b["fk"]):
        va, vb = a["fk"].get(bone, (0, 0, 0)), b["fk"].get(bone, (0, 0, 0))
        out["fk"][bone] = tuple(_lerp(x, y, s) for x, y in zip(va, vb))
    return out


def _breathe(p, t, amp=1.5, head=0.0):
    w = 2 * math.pi * t
    fk = dict(p["fk"])
    for bone, d in (("spine_02", amp), ("spine_03", -amp)):
        x, y, z = fk.get(bone, (0, 0, 0))
        fk[bone] = (x + d * math.sin(w), y, z)
    if head:
        x, y, z = fk.get("head", (0, 0, 0))
        fk["head"] = (x + head * math.sin(w), y, z)
    out = dict(p)
    out["fk"] = fk
    return out


# Each clip: (frames, loops, function(ctx, t) -> pose)
IK_CLIPS = {
    "SitDown": (18, False, lambda c, t: _mix(_standing(c), _sit(c), _smooth(t))),
    "SitIdle": (48, True, lambda c, t: _breathe(_sit(c), t, 1.5, 1.5)),
    "StandUp": (15, False, lambda c, t: _mix(_sit(c), _standing(c), _smooth(t))),
    "LieDown": (48, False, lambda c, t: _mix(_standing(c), _lie(c), _smooth(t))),
    "LieIdle": (72, True, lambda c, t: _breathe(_lie(c), t, 2.0, 1.5)),
    "Sleep": (96, True, lambda c, t: _breathe(_sleep(c), t, 2.5)),
    "GetUp": (36, False, lambda c, t: _mix(_lie(c), _standing(c), _smooth(t))),
}


# ---------------------------------------------------------------- IK rig

# Seed rotations so the planar IK bends elbows back and knees forward.
SEED = {"upper_arm": -10, "forearm": 20, "thigh": -20, "shin": 30}
TARGETS = ("wrist", "ftoe", "hock", "ball", "htoe", "aim_u", "aim_f", "aim_t", "aim_s")


def _setup(arm):
    empties = {}
    for s in SIDES:
        for k in TARGETS:
            e = bpy.data.objects.new(f"IK_{k}.{s}", None)
            bpy.context.scene.collection.objects.link(e)
            empties[f"{k}.{s}"] = e
    pb = arm.pose.bones
    for s in SIDES:
        for bone, target, chain in (("forearm", "wrist", 2), ("shin", "hock", 2)):
            c = pb[f"{bone}.{s}"].constraints.new("IK")
            c.target, c.chain_count, c.use_stretch = empties[f"{target}.{s}"], chain, False
        # "Straight": upper arm and forearm each aim at a point (straight down
        # when sitting; elbow to the ground and forearm forward when lying).
        # Blended against the arm IK by the pose's "straight" weight.
        for bone, aim in (("upper_arm", "aim_u"), ("forearm", "aim_f"),
                          ("thigh", "aim_t"), ("shin", "aim_s")):
            c = pb[f"{bone}.{s}"].constraints.new("DAMPED_TRACK")
            c.name, c.target, c.influence = "Straight", empties[f"{aim}.{s}"], 0.0
        for bone, target in (("front_paw", "ftoe"), ("hind_foot", "ball"), ("hind_toe", "htoe")):
            c = pb[f"{bone}.{s}"].constraints.new("DAMPED_TRACK")
            c.target = empties[f"{target}.{s}"]
        for bone in ("upper_arm", "forearm", "thigh", "shin"):
            pb[f"{bone}.{s}"].lock_ik_y = True
            pb[f"{bone}.{s}"].lock_ik_z = True
    return empties


def _teardown(arm, empties):
    for pb in arm.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
        pb.lock_ik_y = pb.lock_ik_z = False
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    for e in empties.values():
        bpy.data.objects.remove(e)


def _apply(arm, empties, pose):
    pb = arm.pose.bones
    for p in pb:
        p.rotation_mode = "XYZ"
        p.rotation_euler = (0, 0, 0)
        p.location = (0, 0, 0)
    pb["pelvis"].location = pose["pelvis_loc"]
    pb["pelvis"].rotation_euler = (R(pose["pelvis_rot"]), 0, 0)
    for bone, rot in pose["fk"].items():
        pb[bone].rotation_euler = tuple(R(a) for a in rot)
    for s in SIDES:
        for bone, deg in SEED.items():
            pb[f"{bone}.{s}"].rotation_euler = (R(deg), 0, 0)
    for k, e in empties.items():
        e.location = pose[k]
    st, ha = pose.get("straight", 0.0), pose.get("hind_aim", 0.0)
    for s in SIDES:
        pb[f"forearm.{s}"].constraints["IK"].influence = 1.0 - st
        pb[f"shin.{s}"].constraints["IK"].influence = 1.0 - ha
        for bone in ("upper_arm", "forearm"):
            pb[f"{bone}.{s}"].constraints["Straight"].influence = st
        for bone in ("thigh", "shin"):
            pb[f"{bone}.{s}"].constraints["Straight"].influence = ha


def _read_local(arm):
    """Final (constraint-solved) local transforms of all deform bones."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = arm.evaluated_get(dg)
    out = {}
    for pb in ev.pose.bones:
        b = pb.bone
        if not b.use_deform:
            continue
        if b.parent:
            rest = b.parent.matrix_local.inverted() @ b.matrix_local
            local = rest.inverted() @ ev.pose.bones[b.parent.name].matrix.inverted() @ pb.matrix
        else:
            local = b.matrix_local.inverted() @ pb.matrix
        out[b.name] = local
    return out


def _body_mask(mesh, skip):
    """Vertices whose dominant bone does not start with any prefix in skip."""
    names = [g.name for g in mesh.vertex_groups]
    mask = np.ones(len(mesh.data.vertices), dtype=bool)
    for v in mesh.data.vertices:
        if v.groups and names[max(v.groups, key=lambda g: g.weight).group].startswith(skip):
            mask[v.index] = False
    return mask


# Which vertices must touch the ground in each held pose (skip = ignored
# bone prefixes) and how deep they may sink.
# Sitting: the rump (pelvis/thigh) touches; legs and tail are ignored.
SIT_RULE = (("tail_", "upper_arm.", "forearm.", "front_paw."), -0.005)
# Lying: folded legs rest on/under the body, so only the trunk decides.
LIE_RULE = (("tail_", "upper_arm.", "forearm.", "front_paw.", "thigh.", "shin.", "hind_"), -0.01)
SIT_THETA_RANGE = (5.0, 60.0)
SIT_SPINE = 45.0  # extra upward bend of the spine when sitting, degrees


def _lowest(mesh, mask):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    m = ev.to_mesh()
    co = np.empty(len(m.vertices) * 3)
    m.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return float(co.reshape(-1, 3)[mask, 2].min())


def _ground_fit(arm, mesh, empties, ctx):
    """Per-model pose solving: sit pitch (rump on the ground) and lie height."""
    def lowest(pose, mask):
        _apply(arm, empties, pose)
        bpy.context.view_layer.update()
        return _lowest(mesh, mask)

    # Sit: more pitch lowers the rump. Bisect for the angle where it touches.
    skip, floor = SIT_RULE
    mask = _body_mask(mesh, skip)
    lo, hi = SIT_THETA_RANGE
    if lowest(_sit(ctx, hi), mask) > floor:
        ctx["sit_theta"] = hi
    else:
        for _ in range(10):
            mid = (lo + hi) / 2
            if lowest(_sit(ctx, mid), mask) > floor:
                lo = mid
            else:
                hi = mid
        ctx["sit_theta"] = lo

    # Tail was intentionally excluded from rump fitting. Fit its angle
    # separately, rather than lifting the whole dog to fix a sunken tail.
    names=[g.name for g in mesh.vertex_groups]
    tail=np.array([bool(v.groups) and names[max(v.groups,key=lambda g:g.weight).group].startswith('tail_') for v in mesh.data.vertices])
    if tail.any():
        from .validate import _eval_coords
        co=np.array([v.co[:] for v in mesh.data.vertices])
        edges=np.array([e.vertices[:] for e in mesh.data.edges])
        length=np.linalg.norm(co[edges[:,0]]-co[edges[:,1]],axis=1)
        keep=(length>1e-5)
        best=(float('inf'),0,30,20)
        for mid,tip in ((30,20),(10,5),(0,0)):
            ctx['sit_tail_02']=mid;ctx['sit_tail_03']=tip
            for delta in range(-60,61,4):
                ctx['sit_tail_adjust']=delta
                low=lowest(_sit(ctx),tail)
                deformed=_eval_coords(mesh)
                ratio=np.abs(np.linalg.norm(deformed[edges[:,0]]-deformed[edges[:,1]],axis=1)[keep]/length[keep]-1)
                score=max(0,-0.002-low)*1000+float(np.percentile(ratio,99))+abs(delta)*0.0001
                if score<best[0]: best=(score,delta,mid,tip)
        _,ctx['sit_tail_adjust'],ctx['sit_tail_02'],ctx['sit_tail_03']=best

    skip, floor = LIE_RULE
    mask = _body_mask(mesh, skip)
    ctx["lift_lie"] = 0.0
    for _ in range(4):
        low = lowest(_lie(ctx), mask) - floor
        if abs(low) < 0.002:
            break
        ctx["lift_lie"] -= low


_CTX_CACHE = {}


def bake(arm, mesh, name, fps=24):
    if name in MANUAL_CLIPS and _load_manual(MANUAL_CLIPS[name][0]):
        return _bake_manual(arm, mesh, name, fps)
    n, _, fn = IK_CLIPS[name]
    # Earlier clips sit in NLA tracks and would override the pose being solved.
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = None
    muted = [tr for tr in ad.nla_tracks if not tr.mute]
    for tr in muted:
        tr.mute = True
    empties = _setup(arm)
    key = (arm.name, mesh.name)
    if key not in _CTX_CACHE:
        ctx = _context(arm, mesh)
        _ground_fit(arm, mesh, empties, ctx)
        _CTX_CACHE[key] = ctx
    ctx = _CTX_CACHE[key]
    frames, prev = [], {}
    try:
        for f in range(n + 1):
            _apply(arm, empties, fn(ctx, f / n))
            bpy.context.view_layer.update()
            fr = {}
            for bone, m in _read_local(arm).items():
                eul = m.to_euler("XYZ", prev.get(bone, Euler()))
                prev[bone] = eul
                fr[bone] = {"rot": tuple(eul)}
                if bone == "pelvis":
                    fr[bone]["loc"] = tuple(m.to_translation())
            frames.append(fr)
    finally:
        _teardown(arm, empties)
        for tr in muted:
            tr.mute = False
    return write_clip(arm, name, frames, fps)


# ---------------------------------------------------------------- hand-made poses

# Poses posed by hand in Blender and saved to blender/poses/<name>.json
# (bone rotations + pelvis location, see docs). When a file exists, these
# clips are built from it instead of the IK solver above:
# clip: (pose file, kind)
MANUAL_CLIPS = {
    "LieDown": ("lie", "in"),
    "LieIdle": ("lie", "hold"),
    "Sleep": ("lie", "sleep"),
    "GetUp": ("lie", "out"),
}
MANUAL_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "poses")
# Extra rotation (degrees) on top of the lie pose for sleeping: head lowered.
SLEEP_EXTRA = {"neck_01": (15, 0, 0), "neck_02": (12, 0, 0), "head": (10, 0, 0)}
_MANUAL_FIT = {}


def _load_manual(pose_name):
    path = os.path.join(MANUAL_DIR, f"{pose_name}.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _set_pose(arm, rots, pelvis_loc):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = rots.get(pb.name, (0, 0, 0))
        pb.location = (0, 0, 0)
    arm.pose.bones["pelvis"].location = pelvis_loc


def _fit_manual(arm, mesh, data):
    """Same joint angles on this model; pelvis drop scaled by hip height, then
    shifted so the lowest vertex sits where it did on the posed model."""
    key = (arm.name, mesh.name, data["name"])
    if key in _MANUAL_FIT:
        return _MANUAL_FIT[key]
    rots = {b: tuple(R(a) for a in v["rot"]) for b, v in data["bones"].items()}
    hh = arm.data.bones["thigh.L"].head_local.z
    loc = Vector(data["bones"]["pelvis"]["loc"]) * (hh / data["ref_hip_height"])
    if arm.get('source_model') != data.get('source_model'):
        from .contact import adapt_manual_front
        rots = adapt_manual_front(arm, rots, loc)
    for _ in range(3):
        _set_pose(arm, rots, loc)
        bpy.context.view_layer.update()
        low = _lowest(mesh, np.ones(len(mesh.data.vertices), dtype=bool))
        if abs(low - data.get("ground_z", 0.0)) < 0.001:
            break
        loc.z += data.get("ground_z", 0.0) - low
    _MANUAL_FIT[key] = (rots, loc)
    return rots, loc


def _bake_manual(arm, mesh, name, fps):
    pose_name, kind = MANUAL_CLIPS[name]
    n = IK_CLIPS[name][0]
    data = _load_manual(pose_name)
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = None
    muted = [tr for tr in ad.nla_tracks if not tr.mute]
    for tr in muted:
        tr.mute = True
    try:
        rots, loc = _fit_manual(arm, mesh, data)
    finally:
        for pb in arm.pose.bones:
            pb.rotation_euler = (0, 0, 0)
            pb.location = (0, 0, 0)
        for tr in muted:
            tr.mute = False

    frames = []
    for f in range(n + 1):
        t = f / n
        w = 2 * math.pi * t
        s = {"in": _smooth(t), "out": 1 - _smooth(t)}.get(kind, 1.0)
        fr = {b: {"rot": tuple(a * s for a in r)} for b, r in rots.items()}
        if kind in ("hold", "sleep"):
            extra = {"spine_02": (2 * math.sin(w), 0, 0), "spine_03": (-2 * math.sin(w), 0, 0)}
            if kind == "sleep":
                extra.update(SLEEP_EXTRA)
            else:
                extra["head"] = (1.5 * math.sin(w), 0, 0)
            for b, d in extra.items():
                base = fr.get(b, {"rot": (0, 0, 0)})["rot"]
                fr[b] = {"rot": tuple(x + R(y) for x, y in zip(base, d))}
        fr.setdefault("pelvis", {"rot": (0, 0, 0)})["loc"] = tuple(loc * s)
        frames.append(fr)
    return write_clip(arm, name, frames, fps)
