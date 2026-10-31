"""Shared clips, written as rotation-only formulas against the standard skeleton.

Sign convention (see skeleton.py): +X turns the bone tip backward/down
(head/neck: nose down, jaw: mouth open), Z swings it sideways, Y twists it.
Angles are in degrees.
Every clip is keyed on every frame and the last frame equals the first.
Loop clips cycle; one-shot clips start and end at the rest pose so they can
blend back to Idle.
"""
import math

import bpy

R = math.radians


def _walk(t):
    """t in [0, 1). Lateral-sequence walk: HL, FL, HR, FR."""
    rot = {}
    phase = {"L": (0.25, 0.0), "R": (0.75, 0.5)}  # (front, hind)
    for s in ("L", "R"):
        ph = 2 * math.pi * (t + phase[s][0])
        sw, lift = math.sin(ph), max(0.0, math.cos(ph))
        rot[f"upper_arm.{s}"] = (-(20 * sw + 10 * lift), 0, 0)
        rot[f"forearm.{s}"] = (55 * lift, 0, 0)
        rot[f"front_paw.{s}"] = (-(35 * lift + max(0.0, -22 * sw)), 0, 0)
        ph = 2 * math.pi * (t + phase[s][1])
        sw, lift = math.sin(ph), max(0.0, math.cos(ph))
        rot[f"thigh.{s}"] = (-(20 * sw + 10 * lift), 0, 0)
        rot[f"shin.{s}"] = (20 * lift, 0, 0)
        rot[f"hind_foot.{s}"] = (-30 * lift, 0, 0)
        rot[f"hind_toe.{s}"] = (25 * lift, 0, 0)
    w = 2 * math.pi * t
    rot["spine_01"] = (0, 4 * math.sin(w), 0)
    rot["spine_03"] = (0, -4 * math.sin(w), 0)
    rot["head"] = (1.5 * math.sin(2 * w), 0, 0)
    rot["tail_01"] = (0, 0, 15 * math.sin(w))
    rot["tail_02"] = (0, 0, 12 * math.sin(w - 0.6))
    rot["tail_03"] = (0, 0, 8 * math.sin(w - 1.2))
    for s in ("L", "R"):
        rot[f"ear_01.{s}"] = (4 * math.sin(2 * w + 0.5), 0, 0)
    return rot


def _idle(t):
    """t in [0, 1). Breathing, small head look-around, ear and tail sway."""
    w = 2 * math.pi * t
    rot = {
        "spine_02": (1.2 * math.sin(2 * w), 0, 0),
        "spine_03": (-1.2 * math.sin(2 * w), 0, 0),
        "neck_01": (0.75 * math.sin(w), 0, 0),
        "head": (1 * math.sin(2 * w + 0.4), 2 * math.sin(w), 0),
        "jaw": (1.5 + 1.5 * math.sin(2 * w), 0, 0),
        "tail_01": (0, 0, 10 * math.sin(2 * w)),
        "tail_02": (0, 0, 8 * math.sin(2 * w - 0.6)),
        "tail_03": (0, 0, 6 * math.sin(2 * w - 1.2)),
    }
    for s, sg in (("L", 1), ("R", -1)):
        rot[f"ear_01.{s}"] = (3 * math.sin(2 * w + sg), 0, 0)
    return rot


def _env(t, rise=0.2, fall=0.8):
    """0 -> 1 -> 0 envelope with smooth ends, for one-shot clips."""
    def smooth(x):
        x = min(max(x, 0.0), 1.0)
        return x * x * (3 - 2 * x)
    return smooth(t / rise) * smooth((1 - t) / (1 - fall))


TAIL_LIFT = 45.0  # degrees the tail base rises while wagging happily


def _tail_wag(rot, w, amp, speed=1, lift=0.0):
    """Side-to-side wag; lift raises the tail first (+X: a hanging tail swings
    back and up) so the wag reads from the front and the sides."""
    rot["tail_01"] = (lift, 0, amp * math.sin(speed * w))
    rot["tail_02"] = (0.4 * lift, 0, 0.8 * amp * math.sin(speed * w - 0.6))
    rot["tail_03"] = (0.2 * lift, 0, 0.6 * amp * math.sin(speed * w - 1.2))
    rot["tail_04"] = (0, 0, 0.4 * amp * math.sin(speed * w - 1.8))


def _trot(t):
    """t in [0, 1). Diagonal pairs: FL+HR, then FR+HL. For toy chasing."""
    rot = {}
    phase = {"L": (0.0, 0.5), "R": (0.5, 0.0)}  # (front, hind)
    for s in ("L", "R"):
        ph = 2 * math.pi * (t + phase[s][0])
        sw, lift = math.sin(ph), max(0.0, math.cos(ph))
        rot[f"upper_arm.{s}"] = (-(22 * sw + 12 * lift), 0, 0)
        rot[f"forearm.{s}"] = (65 * lift, 0, 0)
        rot[f"front_paw.{s}"] = (-(40 * lift + max(0.0, -24 * sw)), 0, 0)
        ph = 2 * math.pi * (t + phase[s][1])
        sw, lift = math.sin(ph), max(0.0, math.cos(ph))
        rot[f"thigh.{s}"] = (-(20 * sw + 12 * lift), 0, 0)
        rot[f"shin.{s}"] = (26 * lift, 0, 0)
        rot[f"hind_foot.{s}"] = (-36 * lift, 0, 0)
        rot[f"hind_toe.{s}"] = (30 * lift, 0, 0)
    w = 2 * math.pi * t
    rot["spine_02"] = (2 * math.sin(2 * w), 0, 0)
    rot["neck_01"] = (-4, 0, 0)
    rot["head"] = (-3 + 2 * math.sin(2 * w), 0, 0)
    _tail_wag(rot, w, 12, 2)
    for s in ("L", "R"):
        rot[f"ear_01.{s}"] = (6 * math.sin(2 * w + 0.8), 0, 0)
    return rot


def _greet(t):
    """t in [0, 1). Body still, head up, fast tail wag (the main motion)."""
    w = 2 * math.pi * t
    rot = {
        "neck_01": (-6, 0, 0),
        "neck_02": (-4, 0, 0),
        "head": (-8 + 1 * math.sin(6 * w), 0, 0),
        "jaw": (4 + 1.5 * math.sin(6 * w), 0, 0),
    }
    _tail_wag(rot, w, 30, 6, lift=TAIL_LIFT)
    for s in ("L", "R"):
        rot[f"ear_01.{s}"] = (-6 + 3 * math.sin(4 * w), 0, 0)
    return rot


def _pet_head(t):
    """One-shot. Leans into the hand, small head rub, slow wag."""
    e, w = _env(t), 2 * math.pi * t
    rot = {
        "neck_02": (6 * e, 0, 0),
        "head": (10 * e, 0, 5 * e * math.sin(2 * w)),
        "jaw": (4 * e, 0, 0),
    }
    _tail_wag(rot, w, 20 * e, 4, lift=0.7 * TAIL_LIFT * e)
    for s in ("L", "R"):
        rot[f"ear_01.{s}"] = (-8 * e, 0, 0)
    return rot


def _pet_chin(t):
    """One-shot. Raises the nose for a chin scratch."""
    e, w = _env(t), 2 * math.pi * t
    rot = {
        "neck_01": (-6 * e, 0, 0),
        "neck_02": (-6 * e, 0, 0),
        "head": (-18 * e, 0, 0),
        "jaw": (3 * e, 0, 0),
    }
    _tail_wag(rot, w, 15 * e, 4, lift=0.7 * TAIL_LIFT * e)
    for s in ("L", "R"):
        rot[f"ear_01.{s}"] = (-6 * e, 0, 0)
    return rot


def _pet_belly(t):
    """One-shot, standing. Body leans in, left hind leg scratch reflex."""
    e, w = _env(t, 0.15, 0.85), 2 * math.pi * t
    rot = {
        "spine_01": (0, 2 * e, 0),
        "spine_02": (0, 3 * e, 0),
        "head": (0, 12 * e, 0),
        "jaw": (4 * e, 0, 0),
        "thigh.L": (-(15 * e + 12 * e * math.sin(6 * w)), 0, 0),
        "shin.L": (30 * e, 0, 0),
        "hind_foot.L": (-30 * e, 0, 0),
    }
    _tail_wag(rot, w, 20 * e, 4, lift=0.7 * TAIL_LIFT * e)
    return rot


def _head_tilt(t):
    """One-shot. Curious head tilt and back."""
    e = _env(t, 0.25, 0.75)
    return {
        "neck_02": (0, 0, 5 * e),
        "head": (0, 0, 18 * e),
        "ear_01.L": (-6 * e, 0, 0),
        "ear_01.R": (4 * e, 0, 0),
    }


CLIPS = {
    # name: (formula, frame count, loops)
    "Idle": (_idle, 72, True),
    "Walk": (_walk, 24, True),
    "Trot": (_trot, 16, True),
    "Greet": (_greet, 48, True),
    "PetHead": (_pet_head, 48, False),
    "PetChin": (_pet_chin, 48, False),
    "PetBelly": (_pet_belly, 60, False),
    "HeadTilt": (_head_tilt, 36, False),
}


def _assign(ad, action):
    ad.action = action
    if hasattr(ad, "action_slot") and action.slots and ad.action_slot is None:
        ad.action_slot = action.slots[0]


def bake(arm, name, fps=24):
    """Bake one formula clip into its own action and NLA track. Returns the action."""
    formula, n, _ = CLIPS[name]
    frames = []
    for f in range(n + 1):
        rot = formula(f / n)
        frames.append({b: {"rot": tuple(R(a) for a in v)} for b, v in rot.items()})
    return write_clip(arm, name, frames, fps)


def write_clip(arm, name, frames, fps=24):
    """Key frames [{bone: {"rot": euler radians, "loc": optional}}] on every
    deform bone (missing bones get rest rotation), then park the action in an
    NLA track so each clip exports as its own glTF animation."""
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = None
    pbs = [pb for pb in arm.pose.bones if pb.bone.use_deform]
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
    keyed_loc = {b for fr in frames for b, v in fr.items() if "loc" in v}
    for f, fr in enumerate(frames):
        for pb in pbs:
            v = fr.get(pb.name, {})
            pb.rotation_euler = v.get("rot", (0, 0, 0))
            pb.keyframe_insert("rotation_euler", frame=f + 1)
            if pb.name in keyed_loc:
                pb.location = v.get("loc", (0, 0, 0))
                pb.keyframe_insert("location", frame=f + 1)
    action = ad.action
    action.name = name
    action.use_fake_user = True
    ad.action = None
    track = ad.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 1, action)
    if hasattr(strip, "action_slot") and action.slots and strip.action_slot is None:
        strip.action_slot = action.slots[0]
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    bpy.context.scene.render.fps = fps
    return action


def activate(arm, action):
    """Play one action alone (NLA muted) for checks or viewing."""
    ad = arm.animation_data
    for tr in ad.nla_tracks:
        tr.mute = True
    _assign(ad, action)
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 1, int(action.frame_range[1])


def deactivate(arm):
    ad = arm.animation_data
    ad.action = None
    for tr in ad.nla_tracks:
        tr.mute = False
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
