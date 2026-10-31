import copy
import json
from pathlib import Path
import re
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "blender"))
from dogrig import RigError
from dogrig.joint_contract import JOINT_MAP, validate, from_contract, to_contract, to_api_point, to_blender_point


class JointContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        text = (ROOT / "docs/04-api-data.md").read_text(encoding="utf-8-sig")
        section = text.split("## 관절 데이터 형식", 1)[1]
        cls.example = json.loads(re.search(r"```json\s*(.*?)```", section, re.S)[1])

    def test_git_example_unchanged_roundtrip(self):
        before = copy.deepcopy(self.example)
        self.assertEqual(to_contract(from_contract(self.example)), self.example)
        self.assertEqual(before, self.example)

    def test_axis_basis_and_mirror(self):
        self.assertEqual(to_blender_point([0, 1, 0]), [0, 0, 1])
        self.assertEqual(to_blender_point([0, 0, 1]), [0, -1, 0])
        for p in [[.1, .2, -.3], [-.1, 0, .6]]:
            self.assertEqual(to_api_point(to_blender_point(p)), p)
        left = to_blender_point([.1, .2, .3])
        right = to_blender_point([-.1, .2, .3])
        self.assertEqual(left[0], -right[0]); self.assertEqual(left[1:], right[1:])

    def test_private_names(self):
        private = from_contract(self.example)
        self.assertEqual(set(private["points"]), set(JOINT_MAP.values()))
        self.assertEqual(private["points"]["tail_start"], to_blender_point(self.example["joints"]["tail_base"]))

    def test_current_server_defaults_and_extra_names(self):
        data = copy.deepcopy(self.example); del data["space"]; del data["skeletonVersion"]
        data["joints"]["additional"] = [0, 0, 0]
        self.assertEqual(validate(data), self.example)

    def test_invalid_versions_spaces_and_shape(self):
        for field, value in [("skeletonVersion", 2), ("skeletonVersion", True), ("space", "world"), ("joints", [])]:
            data = copy.deepcopy(self.example); data[field] = value
            with self.assertRaises(RigError): validate(data)
        for value in [None, [], "joints"]:
            with self.assertRaises(RigError): validate(value)

    def test_each_missing_joint_rejected(self):
        for name in JOINT_MAP:
            data = copy.deepcopy(self.example); del data["joints"][name]
            with self.assertRaises(RigError): validate(data)

    def test_non_finite_boolean_text_and_wrong_length_rejected(self):
        for value in [[float("nan"), 0, 0], [float("inf"), 0, 0], [True, 0, 0], ["0", 0, 0], [0, 0], None]:
            data = copy.deepcopy(self.example); data["joints"]["nose"] = value
            with self.assertRaises(RigError): validate(data)

    def test_tag_prevents_double_conversion(self):
        private = from_contract(self.example)
        with self.assertRaises(RigError): from_contract(private)
        with self.assertRaises(RigError): to_contract(self.example)
        private["space"] = "normalized"
        with self.assertRaises(RigError): to_contract(private)

    def test_supplied_landmarks_convert_without_private_fields_leaking(self):
        for file in (ROOT / "blender/fixtures/rigged").glob("*/landmarks.json"):
            source = json.loads(file.read_text(encoding="utf-8-sig"))
            public = to_contract(source)
            self.assertEqual(set(public), {"skeletonVersion", "space", "joints"})
            self.assertEqual(set(public["joints"]), set(JOINT_MAP))
            self.assertEqual(from_contract(public)["points"], source["points"])


if __name__ == "__main__":
    unittest.main()
