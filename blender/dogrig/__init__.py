"""Dog auto-rig pipeline (skeletonVersion 1, temporary landmark format)."""

SKELETON_VERSION = 1
LANDMARK_FORMAT = "dog-landmarks-tmp/0"


class RigError(Exception):
    """Pipeline failure with a machine-readable code."""

    def __init__(self, code, message):
        super().__init__(f"{code}: {message}")
        self.code = code
        self.message = message
