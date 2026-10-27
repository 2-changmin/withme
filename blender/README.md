# blender

황세인 담당. 표준 뼈대(`skeleton.blend`), 공용 애니메이션(`animations.blend`), 작업자가 실행하는 스크립트(`prepare.py`, `rig.py`)를 둔다.

작업자는 `blender -b --factory-startup -P <script> -- <args>`로 실행한다 (`backend/app/worker/blender.py`).
