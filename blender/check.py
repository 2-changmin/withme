"""작업자 환경 점검: Blender 헤드리스로 큐브를 만들어 GLB로 내보낸다 (#28)

blender -b --factory-startup -P check.py -- <out.glb>
성공하면 마지막 줄에 "CHECK_OK <버전> <파일 크기>"를 출력한다.
"""
import os
import sys

import bpy

out = sys.argv[sys.argv.index("--") + 1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_cube_add()
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB")

print("CHECK_OK", bpy.app.version_string, os.path.getsize(out))
