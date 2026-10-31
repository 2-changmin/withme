# 표준 뼈대 v1

- `skeletonVersion: 1`, 33개 뼈. 정의: `blender/dogrig/skeleton.py`의 `BONES`·`layout`.
- 기준 템플릿: `blender/skeleton.blend`. Blender 4.2.0에서 생성·저장·다시 열기·33개 뼈·0.01 미만 길이 없음 확인.
- 현재 상태: 로컬 파일·구조 및 3체형·17클립 공용/모델별 Generic 102경우 검증 통과. 실제 모델 품질은 미승인이다. 기존 #7의 25뼈 샘플을 이 규격으로 재분류하지 않는다.

## 좌표와 관절

공개 입력은 [Git 관절 JSON](04-api-data.md#관절-데이터-형식)의 정면 +Z·위 +Y·키 1·바닥 y=0이다. Blender 내부는 정면 -Y·위 +Z·키 1·바닥 z=0이며 [입력 경계](rig-joint-contract.md)에서 한 번 변환한다. GLB 내보내기는 `export_yup=True`다. Unity 임포터의 x 부호 반전을 포함한 실제 좌표 최대 오차 `2.46207577e-7`을 확인했다.

`.L`은 개의 왼쪽(+X), `.R`은 오른쪽(-X)이며 기준점의 x 대칭으로 구성한다. 정규화 길이는 실제 cm가 아니다.

## 이름·부모

| 뼈 | 부모 |
|---|---|
| root | 없음, 이동용·비변형 |
| pelvis | root |
| spine_01 → spine_02 → spine_03 | pelvis에서 순서대로 연결 |
| neck_01 → neck_02 → head | spine_03에서 순서대로 연결 |
| jaw | head |
| ear_01.L → ear_02.L | head에서 연결 |
| ear_01.R → ear_02.R | head에서 연결 |
| tail_01 → tail_02 → tail_03 → tail_04 | pelvis에서 연결 |
| shoulder.L → upper_arm.L → forearm.L → front_paw.L | spine_03에서 연결 |
| shoulder.R → upper_arm.R → forearm.R → front_paw.R | spine_03에서 연결 |
| thigh.L → shin.L → hind_foot.L → hind_toe.L | pelvis에서 연결 |
| thigh.R → shin.R → hind_foot.R → hind_toe.R | pelvis에서 연결 |

## 기본 자세·회전축·체형

- 기본 자세는 네 발로 선 자세다. 메시를 붙이기 전에 모델별 머리·몸통·목·다리·귀·꼬리 길이와 위치를 맞춘다.
- roll은 단순 숫자 복사가 아니라 뼈 방향에 대해 로컬 X축을 월드 +X로 정렬하는 규칙을 사용한다. X와 거의 평행한 귀에는 예외가 있다. 체형 변경 후 축·기본 회전을 비교하고 불일치는 공용 동작 보정으로 다룬다.
- 중간 관절은 기준점·길이 비율로 배치한다. `MIN_BONE_LENGTH=0.01` 미만이면 실패 처리한다. 없는 귀·꼬리 메시를 새로 만들지 않는다.
- 모델마다 기본 뼈 길이·위치는 다르다. 골반 절대 이동이 포함된 동작을 다른 모델에 그대로 덮어쓰지 않는다.

## 애니메이션 채널

모든 클립에 33개 회전 채널을 남긴다. 스케일·다른 뼈의 위치 채널은 내보내지 않고, 모델별 접지/자세에 필요한 골반 위치만 허용한다. 공용 Generic 직접 재사용·골반 보정·반복 및 일회성 재생은 Unity에서 실제 검증 후 호환성을 기록한다.

뼈 이름·계층·좌표·축 의미가 달라지면 기존 v1과 조용히 혼용하지 않고 변경/버전 합의를 기록한다. #17의 장세찬 공동 합의 완료를 이 로컬 문서 작성으로 대신하지 않는다.
