# dog-rig

> 2026-10-30 contact-v2 업데이트: [CORRECTIONS.md](CORRECTIONS.md)를 먼저 확인하세요.
> 기본 출력은 `output_corrected/`, 클립은 수면 전환을 포함해 17개입니다.
> 아래 15개 클립·기존 output 수치는 이전 상태 기록입니다. 최신 수치는 output_corrected의 report.json과 audit 검사 결과를 따릅니다.

Meshy 강아지 GLB를 표준 뼈대(33개)로 자동 리깅하고 공용 동작(Idle, Walk)을 넣어 GLB로 내보내는 Blender 스크립트. LLM 없이 Python만으로 동작한다.

- Blender 5.2.3 LTS에서 확인
- 계획 문서: `work-plan.md` 단계 A~C의 초안 + E의 일부(자동 추정)

## 폴더

```
blender/
  rig.py                   진입점: GLB 1개 → 리깅 GLB + 보고서
  estimate_landmarks.py    기준점만 추정해 JSON으로 저장
  landmarks_from_blend.py  Blender 안에서 실행: LM_* 빈 객체 → landmarks.json
  dogrig/
    prepare.py      가져오기, 정규화(정면 -Y, 높이 1, 바닥 0)
    landmarks.py    기준점 자동 추정 / JSON 읽기·쓰기
    skeleton.py     표준 뼈대 33개, 기준점 → 뼈 위치, roll 규칙
    weights.py      복셀 프록시로 자동 웨이트 → 원본 전달 → 빈 점 보정
    animations.py   Idle, Walk (회전 전용 수식)
    validate.py     빈 웨이트, 뼈 위치, 바닥 뚫림·뜸, 늘어남 검사
    export.py       GLB 내보내기 (클립당 애니메이션 1개, 회전 채널만)
docs/               뼈대 규격, 기준점 형식
tools/glb_info.py   GLB 구조 확인 (뼈, 애니메이션 채널)
input/              원본 GLB
output/<모델>/      리깅 GLB, landmarks.json, report.json, 검토용 .blend
run_all.ps1         input/의 모든 GLB 처리
```

## 실행

모든 모델 처리:

```powershell
.\run_all.ps1
```

수정한 기준점으로 다시 처리 (`output\<모델>\landmarks.json` 사용):

```powershell
.\run_all.ps1 -UseLandmarks
```

모델 1개:

```powershell
& "C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b --factory-startup -P blender\rig.py -- input\m2_white.glb output\m2_white\m2_white_rigged.glb --landmarks-out output\m2_white\landmarks.json --report output\m2_white\report.json --blend output\m2_white\m2_white.blend
```

종료 코드: 0 성공, 2 경고 포함 성공, 1 실패. 실패 원인은 `report.json`의 `error.code`
(`NO_MESH`, `LEGS_NOT_FOUND`, `TAIL_NOT_FOUND`, `EARS_NOT_FOUND`, `ZERO_LENGTH_BONE`, `BONE_HEAT_FAILED`, `LANDMARKS_INVALID`, `UNEXPECTED`).

## 현재 결과 (2026-10-30)

## 클립

| 클립 | 프레임 | 반복 | 내용 | 계획서 행동 |
|---|---|---|---|---|
| Idle | 72 | 반복 | 호흡, 고개 미세 움직임, 귀·꼬리 | 대기 |
| Walk | 24 | 반복 | 제자리 걷기 (HL→FL→HR→FR) | 걷기 |
| Trot | 16 | 반복 | 대각선 빠른 걸음 | 장난감 쫓기 |
| Greet | 48 | 반복 | 몸 고정, 고개 들기, 꼬리 45° 들고 빠르게 흔들기(±30°, 초당 3회) | 반기기 |
| PetHead | 48 | 1회 | 손에 머리 기대기, 귀 젖히기 | 쓰다듬기(머리) |
| PetChin | 48 | 1회 | 코를 들어 턱 내밀기 | 쓰다듬기(턱) |
| PetBelly | 60 | 1회 | 선 채로 몸 기울이고 왼쪽 뒷다리 긁기 반사 | 쓰다듬기(배) |
| HeadTilt | 36 | 1회 | 고개 갸웃 후 복귀 | 고개 갸웃 |
| SitDown | 18 | 1회 | 서기 → 앉기 | 앉기 |
| SitIdle | 48 | 반복 | 앉은 채 호흡 | 앉기 |
| StandUp | 15 | 1회 | 앉기 → 서기 | 앉기 |
| LieDown | 48 | 1회 | 서기 → 엎드리기 (앞발 뻗은 자세) | 엎드리기 |
| LieIdle | 72 | 반복 | 엎드린 채 호흡 | 엎드리기 |
| Sleep | 96 | 반복 | 엎드려 고개 숙이고 자기 | 자기 |
| GetUp | 36 | 1회 | 엎드리기 → 서기 | 엎드리기 |

- 1회 클립 중 반응·갸웃은 기본 자세에서 시작해 기본 자세로 끝난다. 끝나면 Idle로 넘기면 된다.
- 연결 순서: Idle → SitDown → SitIdle → StandUp → Idle, Idle → LieDown → LieIdle ↔ Sleep → GetUp → Idle.
  LieIdle과 Sleep은 고개만 다르므로 크로스페이드로 오간다.
- 앉기·엎드리기 계열(`dogrig/poses.py`)은 모델마다 IK로 풀어서 굽는다. 골반을 내리고 발·발목·발끝을
  바닥 목표점에 고정한 뒤, 결과를 회전값(+ 골반 위치)으로 저장하고 IK는 지운다.
  - 앉기: 앞다리의 위팔·아래팔은 수직 일자로 편다("Straight" 제약). 다리 길이 차이는 몸에 붙은 첫 마디인
    견갑골을 ±30° 안에서 기울여 맞추고, 그래도 모자라면 몸 전체를 올린다. 골반을 기울이고 척추를 45° 더 세운다.
    골반 기울기는 엉덩이가 바닥에 닿는 각도를 모델마다 이분 탐색으로 찾는다(`report.json`의 `ground_fit.sit_theta`,
    약 12~22°, 몸 전체로는 약 57~67°). 엉덩이가 앞으로 오는 만큼 뒷발도 엉덩이 아래 앞쪽에 놓는다.
  - 엎드리기: 몸통 아래가 바닥에 닿도록 골반 높이를 메시로 맞춘다.
    앞다리는 위팔이 어깨에서 아래(또는 앞)로 내려가 팔꿈치가 바닥에 닿고, 아래팔은 팔꿈치에서 바닥을 따라 앞으로 뻗는다.
    팔꿈치가 어깨보다 뒤로 가지 않게 해서 아래팔이 위팔 밑을 가로지르지 않는다. 어깨 높이는 견갑골을 최대 45° 기울여 맞춘다.
    뒷다리는 허벅지가 골반에서 뒤로 비스듬히 내려가 무릎이 골반 뒤쪽 바닥에 닿는다(옆으로 벌리지 않음).
    무릎부터 정강이·발꿈치·발은 모두 바닥을 따라 앞으로 뻗는다.
    무릎·팔꿈치 위치는 IK 대신 직접 계산해 각 뼈가 그 점을 향하게 한다. 꼬리는 들어서 바닥 위로 둔다(`LIE_TAIL`).

## 손으로 잡은 포즈 (`blender/poses/`)

`blender/poses/<이름>.json`이 있으면 해당 클립은 IK 계산 대신 그 포즈로 만든다(`poses.MANUAL_CLIPS`).

| 파일 | 쓰는 클립 | 만든 곳 |
|---|---|---|
| `lie.json` | LieDown, LieIdle, Sleep, GetUp | m2_white에서 Blender로 직접 포즈 (2026-10-30) |

- 저장 내용: 변형 뼈 32개의 회전(오일러 XYZ, 도)과 골반 위치, 포즈를 잡은 모델의 골반 높이(`ref_hip_height`), 가장 낮은 점 높이(`ground_z`).
- 다른 모델: 같은 관절 각도를 쓰고, 골반 내림 양을 골반 높이 비율로 늘이거나 줄인 뒤 가장 낮은 점이 `ground_z`에 오도록 맞춘다.
- 전환(LieDown, GetUp)은 서 있는 자세 ↔ 이 포즈를 각도 그대로 보간한다. Sleep은 이 포즈에서 고개만 더 숙인다(`SLEEP_EXTRA`).
- 파일을 지우면 IK 계산 방식(`_lie`)으로 돌아간다.
- 새 포즈 저장: Blender에서 리그를 포즈 모드로 잡은 상태에서 각 뼈의 `rotation_euler`(도)와 `pelvis`의 `location`을 같은 형식으로 저장한다.

## 내보내기 채널

- 모든 클립에 33개 뼈 회전 + 골반 위치가 들어간다. 스케일과 다른 뼈의 위치는 없다(`export.strip_channels`).
- 골반 위치는 이 모델의 다리 길이로 계산한 값이라 뼈 길이를 덮어쓰지 않는다.
- 변하지 않는 채널도 남겨 둔다. 빼면 SitIdle 같은 유지 자세가 서 있는 자세로 돌아간다.

## 현재 결과

| 모델 | 상태 | 경고 |
|---|---|---|
| m1_golden | warning | 늘어남(꼬리 털), 앉아 있기 바닥 -7.6cm, 엎드려 있기 -2.8cm |
| m2_white | warning | 늘어남(꼬리 털), 앉아 있기 -4.4cm, 엎드려 있기 -4.2cm |
| m3_cream | warning | 늘어남(꼬리 털), 앉아 있기 -3.8cm, 엎드려 있기 -3.0cm |

- LieDown·GetUp 전환 중간에는 다리가 접히면서 바닥을 6~13cm 더 뚫는 순간이 있다.

- 앉기·엎드리기의 바닥 뚫림은 의도한 값이다. 엉덩이 살(앉기)과 접힌 다리(엎드리기)는 메시가 눌리지 않아서
  바닥 아래로 일부 들어가게 두었다. 바닥에 그림자만 있으면 보이지 않는 수준인지 Unity에서 확인한다.
- SitDown·GetUp 전환 중간에 늘어남 수치가 높게 나온다(1.1~1.5). 다리가 접히는 순간이라 Blender에서 눈으로 확인 필요.

- 늘어남 수치는 꼬리·다리의 긴 털 가닥에서 크게 나온다. 경고는 참고용이고 Blender에서 눈으로 판단한다.
- 귀 가중치는 귀 주변으로 제한한다(`weights.EAR_RULES`). 늘어진 귀가 목을 끌고 가던 문제 때문이다.

## 한계 (확인 필요)

- 기준점 자동 추정은 서 있는 네발 강아지, 정면이 머리 쪽이 더 높은 모델만 가정한다.
- 귀·꼬리 추정은 대략적이다. 털이 많은 모델은 관절이 털 기준으로 잡힐 수 있다.
- 세 모델의 체형 비율이 비슷해 짧은 다리·긴 몸통 체형은 미검증이다.
- Unity PC 재생(단계 D)은 아직 하지 않았다.
- 걷기·빠른 걸음의 발 접지는 검사만 하고 IK 보정은 없다(앉기·엎드리기만 IK 사용).
- m1, m3는 귀 끝 추정이 머리 안쪽으로 잡혀 늘어진 귀 끝이 덜 흔들린다.
