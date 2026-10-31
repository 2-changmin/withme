# 표준 뼈대 규격 (skeletonVersion: 1, 초안)

- 상태: 초안. 3개 모델(m1_golden, m2_white, m3_cream)에서 Blender 확인만 했고 Unity PC 재생은 아직 확인하지 않음.
- 정의 위치: `blender/dogrig/skeleton.py` (`BONES`, `layout`)

## 좌표계

| 구분 | 정면 | 위 | 왼쪽(개 기준) |
|---|---|---|---|
| Blender 내부 | -Y | +Z | +X |
| GLB(glTF) | +Z | +Y | +X |

- 변환은 glTF 내보내기에서 한 번만 적용한다(`export_yup=True`).
- 모델은 높이 1, 발바닥 z=0, 앞뒤·좌우 중심 0으로 정규화한다. 적용한 변환은 `report.json`의 `normalize`에 기록한다.

## 뼈 목록 (33개)

`root`는 변형하지 않는 이동용 뼈이고, 나머지 32개가 변형 뼈다.

| 영역 | 뼈 (부모) |
|---|---|
| 중심 | `root`, `pelvis`(root), `spine_01`(pelvis), `spine_02`, `spine_03` |
| 목·머리 | `neck_01`(spine_03), `neck_02`, `head`, `jaw`(head) |
| 귀 | `ear_01.L`(head), `ear_02.L`, `ear_01.R`(head), `ear_02.R` |
| 꼬리 | `tail_01`(pelvis), `tail_02`, `tail_03`, `tail_04` |
| 앞다리 ×2 | `shoulder.X`(spine_03), `upper_arm.X`, `forearm.X`, `front_paw.X` |
| 뒷다리 ×2 | `thigh.X`(pelvis), `shin.X`, `hind_foot.X`, `hind_toe.X` |

- 좌우 표기: `.L` = 개의 왼쪽(+X), `.R` = 오른쪽(-X).
- 괄호가 없는 뼈의 부모는 바로 위 뼈다.

## 축 규칙 (bone roll)

기본 자세에서 **모든 뼈의 로컬 X축이 월드 +X(개의 왼쪽)를 향하도록** roll을 정한다.

| 회전 | 의미 |
|---|---|
| +X | 뼈 끝이 뒤/아래로 (다리가 뒤로, 고개가 숙여짐) |
| Z | 뼈 끝이 옆으로 (꼬리 흔들기, 고개 갸웃) |
| Y | 뼈 길이 방향 비틀기 (몸통 롤, 고개 돌리기) |

- 좌우 다리가 같은 부호로 같은 방향으로 굽는다(좌우 반전 처리 불필요).
- 예외: X축과 거의 평행한 뼈(옆으로 뻗은 귀 등)는 Z를 위로 둔다. 이 경우 +X 규칙이 정확히 맞지 않을 수 있다.
- roll 숫자를 복사하지 않고, 뼈 방향에서 매번 계산한다(`skeleton._roll_target`).

## 체형 맞춤

- 뼈의 시작·끝 위치를 기준점(`landmarks.json`)에서 계산하므로 뼈 길이는 모델마다 다르다.
- 중간 관절(팔꿈치·무릎·발목)은 다리 높이에 대한 고정 비율로 놓는다. 실제 수치는 `skeleton.layout` 참고.
- 길이가 0.01(정규화 단위) 미만인 뼈가 생기면 `ZERO_LENGTH_BONE`으로 실패 처리한다.

## 동작 클립

- 모든 클립에 33개 뼈 회전 채널과 `pelvis` 위치 채널만 내보낸다. 스케일과 다른 뼈의 위치 채널은 없으므로 클립이 모델의 뼈 길이를 덮어쓰지 않는다.
- `pelvis` 위치는 앉기·엎드리기에서 이 모델의 다리 길이로 계산한 값이다. 다른 클립에서는 기본 위치로 고정된다.
- 움직이지 않는 뼈의 채널도 남긴다. 클립 전환 시 이전 자세가 남지 않는다.
- 클립 목록과 반복 여부는 `README.md` 참고. 정의는 `blender/dogrig/animations.py`의 `CLIPS`.
