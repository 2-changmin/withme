# blender

황세인 담당. 표준 뼈대(`skeleton.blend`), 공용 애니메이션(`animations.blend`), 작업자가 실행하는 스크립트(`prepare.py`, `rig.py`)를 둔다.

작업자는 `blender -b --factory-startup -P <script> -- <args>`로 실행한다 (`backend/app/worker/blender.py`).

사용자 제공 `dog-rig.zip` 수령 이후에는 [리깅·동작·Unity 일괄 실행 계획](../docs/batch-implementation-plan.md)을 따른다. Git 관절 JSON 어댑터, Blender 버전 호환, 품질 보완, 공용 클립과 Unity PC 검증을 작은 작업으로 나눴다. 첨부 임시 JSON을 외부 API 계약으로 채택하지 않는다.

Blender 4.2.0에서 `skeleton.blend`·`animations.blend`·`prepare.py`·Git JSON CLI `rig.py`를 생성·검증했다. 3모델 메시를 2만 삼각형 이하로 줄이고 재리깅·재현성 검사를 통과했지만 품질 경고 13/5/18개가 남아 모두 미승인이다. 기존 로컬 `work-plan.md`는 보존했다.

- [인수 결과·남은 품질](../docs/batch-acceptance.md), [진행 기록](../docs/batch-progress.md), [실제 worker 검토 자료](../docs/rig-worker-integration.md).
- 로컬 어댑터는 `worker_adapter.py`, 정식 CLI는 `blender -b --factory-startup -P blender/rig.py -- IN.glb JOINTS.json OUT.glb --report REPORT.json`이다.
- 종료 2는 검토 필요 산출물이며 품질 승인이 아니다. 실제 backend/배포의 종료 코드 처리는 별도 담당자 확인 항목이다.
