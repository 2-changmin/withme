"""Standard skeleton v1: 33 bones fitted to landmarks.

Axis rule: every bone's local X axis points to world +X (the dog's left) in
rest pose, so a positive X rotation always turns the bone tip backward/down
(legs swing back, head nods down). Sideways swing is local Z, twist is local Y.
"""
import bpy
from mathutils import Vector

from . import RigError

# (name, parent, deform). Order is also the export joint order.
BONES = [
    ("root", None, False),
    ("pelvis", "root", True),
    ("spine_01", "pelvis", True),
    ("spine_02", "spine_01", True),
    ("spine_03", "spine_02", True),
    ("neck_01", "spine_03", True),
    ("neck_02", "neck_01", True),
    ("head", "neck_02", True),
    ("jaw", "head", True),
    ("ear_01.L", "head", True),
    ("ear_02.L", "ear_01.L", True),
    ("ear_01.R", "head", True),
    ("ear_02.R", "ear_01.R", True),
    ("tail_01", "pelvis", True),
    ("tail_02", "tail_01", True),
    ("tail_03", "tail_02", True),
    ("tail_04", "tail_03", True),
]
for _s in ("L", "R"):
    BONES += [
        (f"shoulder.{_s}", "spine_03", True),
        (f"upper_arm.{_s}", f"shoulder.{_s}", True),
        (f"forearm.{_s}", f"upper_arm.{_s}", True),
        (f"front_paw.{_s}", f"forearm.{_s}", True),
        (f"thigh.{_s}", "pelvis", True),
        (f"shin.{_s}", f"thigh.{_s}", True),
        (f"hind_foot.{_s}", f"shin.{_s}", True),
        (f"hind_toe.{_s}", f"hind_foot.{_s}", True),
    ]

# Bones whose head sits on the parent's tail.
CONNECTED = {
    "spine_02", "spine_03", "neck_01", "neck_02", "head", "ear_02.L", "ear_02.R",
    "tail_02", "tail_03", "tail_04",
    "upper_arm.L", "forearm.L", "front_paw.L", "shin.L", "hind_foot.L", "hind_toe.L",
    "upper_arm.R", "forearm.R", "front_paw.R", "shin.R", "hind_foot.R", "hind_toe.R",
}

MIN_BONE_LENGTH = 0.01


def _v(p):
    return Vector(p)


def _mirror(p):
    return Vector((-p.x, p.y, p.z))


def layout(lm):
    """Return {bone: (head, tail)} from landmark data."""
    P = {k: _v(v) for k, v in lm["points"].items()}
    S, FP, H, HP = P["front_shoulder_l"], P["front_paw_l"], P["hind_hip_l"], P["hind_paw_l"]
    N, NO, ER, ET = P["neck"], P["nose"], P["ear_root_l"], P["ear_tip_l"]
    B = {}

    chest = Vector((0, S.y, S.z))
    pel = Vector((0, H.y, H.z + 0.04))
    body = (chest - pel).length
    fwd = (chest - pel).normalized()
    mid_y = (S.y + H.y) / 2
    B["root"] = (Vector((0, mid_y, 0)), Vector((0, mid_y - 0.25 * body, 0)))
    B["pelvis"] = (pel.copy(), pel - fwd * 0.15 * body)
    s1, s2 = pel.lerp(chest, 0.4), pel.lerp(chest, 0.72)
    B["spine_01"] = (pel.copy(), s1)
    B["spine_02"] = (s1, s2)
    B["spine_03"] = (s2, chest.copy())

    nmid = chest.lerp(N, 0.5)
    B["neck_01"] = (chest.copy(), nmid)
    B["neck_02"] = (nmid, N.copy())
    hl = (NO - N).length
    head_tip = Vector((0, N.y + 0.3 * (NO.y - N.y), N.z + 0.75 * hl))
    B["head"] = (N.copy(), head_tip)
    B["jaw"] = (N + (NO - N) * 0.25 - Vector((0, 0, 0.1 * hl)),
                Vector((0, NO.y + 0.05 * hl, NO.z - 0.25 * hl)))

    emid = ER.lerp(ET, 0.5)
    B["ear_01.L"], B["ear_02.L"] = (ER.copy(), emid), (emid, ET.copy())
    B["ear_01.R"], B["ear_02.R"] = (_mirror(ER), _mirror(emid)), (_mirror(emid), _mirror(ET))

    curve = lm.get("hints", {}).get("tail_curve")
    T0, T1 = P["tail_start"], P["tail_end"]
    if curve and len(curve) == 5:
        cp = [_v(c) for c in curve]
        cp[0], cp[4] = T0, T1
    else:
        cp = [T0.lerp(T1, k / 4) for k in range(5)]
    for i in range(4):
        B[f"tail_0{i + 1}"] = (cp[i].copy(), cp[i + 1].copy())

    hs = S.z - FP.z
    hh = H.z - HP.z
    left = {
        "shoulder": (Vector((0.45 * S.x, S.y + 0.03 * hs, S.z + 0.05 * hs)),
                     Vector((S.x, S.y - 0.02 * hs, S.z - 0.35 * hs))),
    }
    elbow = Vector((S.x * 0.3 + FP.x * 0.7, (S.y + FP.y) / 2, S.z - 0.62 * hs))
    wrist = Vector((FP.x, FP.y, FP.z + 0.13 * hs))
    ftoe = Vector((FP.x, FP.y - 0.22 * hs, FP.z + 0.035 * hs))
    left["upper_arm"] = (left["shoulder"][1].copy(), elbow)
    left["forearm"] = (elbow.copy(), wrist)
    left["front_paw"] = (wrist.copy(), ftoe)

    knee = Vector((HP.x, H.y - 0.10 * hh, H.z - 0.42 * hh))
    hock = Vector((HP.x, HP.y + 0.12 * hh, HP.z + 0.28 * hh))
    ball = Vector((HP.x, HP.y + 0.05 * hh, HP.z + 0.07 * hh))
    htoe = Vector((HP.x, HP.y - 0.15 * hh, HP.z + 0.03 * hh))
    left["thigh"] = (H.copy(), knee)
    left["shin"] = (knee.copy(), hock)
    left["hind_foot"] = (hock.copy(), ball)
    left["hind_toe"] = (ball.copy(), htoe)

    for k, (h, t) in left.items():
        B[f"{k}.L"] = (h, t)
        B[f"{k}.R"] = (_mirror(h), _mirror(t))
    return B


def _roll_target(d):
    z = Vector((1, 0, 0)).cross(d)
    if z.length > 0.2:
        return z
    # Bone almost parallel to X (e.g. ears sticking out): keep Z up/forward.
    return Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((0, -1, 0))


def build(lm, name="Dog_Rig"):
    B = layout(lm)
    short = {k: round((t - h).length, 4) for k, (h, t) in B.items() if (t - h).length < MIN_BONE_LENGTH}
    if short:
        raise RigError("ZERO_LENGTH_BONE", f"bones too short: {short}")

    data = bpy.data.armatures.new(name)
    arm = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = data.edit_bones
    for bname, parent, deform in BONES:
        h, t = B[bname]
        e = eb.new(bname)
        e.head, e.tail = h, t
        e.use_deform = deform
        e.align_roll(_roll_target((t - h).normalized()))
        if parent:
            e.parent = eb[parent]
            e.use_connect = bname in CONNECTED and (eb[parent].tail - h).length < 1e-4
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.data.display_type = "STICK"
    arm.show_in_front = True
    return arm


def bone_lengths(arm):
    return {b.name: round(b.length, 4) for b in arm.data.bones}
