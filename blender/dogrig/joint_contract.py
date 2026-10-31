"""Git API joints (Y-up/+Z-front) <-> private Blender landmarks (Z-up/-Y-front).

This module has no Blender/numpy dependency so the boundary can be checked before
starting an expensive process. Only docs/04-api-data.md is a public JSON format.
"""
import math
from . import RigError, LANDMARK_FORMAT, SKELETON_VERSION

JOINT_MAP = {
    "nose": "nose", "neck": "neck", "tail_base": "tail_start",
    "tail_tip": "tail_end", "front_paw_l": "front_paw_l",
    "shoulder_l": "front_shoulder_l", "hind_paw_l": "hind_paw_l",
    "hip_l": "hind_hip_l", "ear_tip_l": "ear_tip_l", "ear_base_l": "ear_root_l",
}
BLENDER_SPACE = "blender: front -Y, up +Z, left +X, height normalized to 1, feet at z=0"


def _error(message):
    raise RigError("JOINTS_INVALID", message)


def vector(value, name):
    if not isinstance(value, (list, tuple)) or len(value) != 3:
        _error(f"{name}: expected three numbers")
    if any(isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) for v in value):
        _error(f"{name}: coordinates must be finite numbers")
    return [float(v) for v in value]


def validate(data):
    if not isinstance(data, dict):
        _error("expected an object")
    version = data.get("skeletonVersion", SKELETON_VERSION)
    if type(version) is not int or version != SKELETON_VERSION:
        _error("unsupported skeletonVersion")
    if data.get("space", "normalized") != "normalized":
        _error("expected normalized Y-up API coordinates")
    joints = data.get("joints")
    if not isinstance(joints, dict) or not JOINT_MAP.keys() <= joints.keys():
        _error("all ten documented joints are required")
    # Current server permits additional names; don't invent a stricter public key set.
    for name, value in joints.items():
        vector(value, name)
    return {"skeletonVersion": version, "space": "normalized",
            "joints": {name: vector(joints[name], name) for name in JOINT_MAP}}


def to_blender_point(value):
    x, y, z = vector(value, "point")
    return [x, -z, y]


def to_api_point(value):
    x, y, z = vector(value, "point")
    return [x, z, -y]


def from_contract(data):
    canonical = validate(data)
    return {"format": LANDMARK_FORMAT, "space": BLENDER_SPACE,
            "source": "git-joints-v1", "hints": {},
            "points": {private: to_blender_point(canonical["joints"][public])
                       for public, private in JOINT_MAP.items()}}


def to_contract(data):
    if not isinstance(data, dict) or data.get("format") != LANDMARK_FORMAT or data.get("space") != BLENDER_SPACE:
        _error("expected explicitly tagged internal Blender landmarks")
    points = data.get("points")
    if not isinstance(points, dict) or not set(JOINT_MAP.values()) <= points.keys():
        _error("internal landmark names missing")
    return validate({"skeletonVersion": SKELETON_VERSION, "space": "normalized",
                     "joints": {public: to_api_point(points[private]) for public, private in JOINT_MAP.items()}})
