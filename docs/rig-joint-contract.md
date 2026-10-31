# 리깅 관절 입력 경계

외부 JSON은 [04-api-data.md](04-api-data.md#관절-데이터-형식)의 `skeletonVersion:1`, `space:"normalized"`, `joints`를 따른다. 첨부 임시 `points` 형식은 Blender 내부에서만 사용한다.

| Git 이름 | Blender 내부 이름 |
|---|---|
| nose / neck | nose / neck |
| tail_base / tail_tip | tail_start / tail_end |
| shoulder_l / hip_l | front_shoulder_l / hind_hip_l |
| front_paw_l / hind_paw_l | front_paw_l / hind_paw_l |
| ear_base_l / ear_tip_l | ear_root_l / ear_tip_l |

- Git 좌표→Blender: `(x,y,z)→(x,-z,y)`. 역변환: `(x,y,z)→(x,z,-y)`.
- x 좌우 대칭은 변환과 독립적이다. 모델 정규화·축 변환을 같은 데이터에 두 번 적용하지 않는다.
- 경계 모듈 `blender/dogrig/joint_contract.py`는 bpy·numpy 없이 입력을 검증한다. 누락 관절·잘못된 벡터·비유한 수·지원하지 않는 버전/공간을 거부한다.
- 현재 서버처럼 생략된 버전/space는 기본값 1/normalized를 사용한다. 추가 관절 이름을 새로 금지하지 않지만 내부 변환 결과에는 정식 10개만 넣는다.
- 단순 숫자·형식 검증 통과는 관절 위치의 해부학적 타당성 검증이 아니다. 실제 메시·Unity 임포터 왕복은 별도로 검사한다.

## 확인한 범위

2026-10-30: Git 예제와 제공 3모델 기준점의 Python 왕복, 방향 기저·x 대칭, 오류 입력·중복 변환 방어 9개 테스트 통과. Unity/GLB 임포터까지의 실제 위치 일치는 아직 미검증이다. 서버 스키마·공개 API는 수정하지 않았다.
