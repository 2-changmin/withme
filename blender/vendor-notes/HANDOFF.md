# 인수인계: 강아지 자동 리깅 파이프라인

> 후속 contact-v2 작업은 CORRECTIONS.md 참고. output_corrected/에 새 결과를 만들며 기존 output/은 보존됩니다. 아래 본문은 보정 전 인수인계입니다.

- 작성: 2026-10-30
- 다음 세션은 이 파일 → `README.md` → `work-plan.md` 순서로 읽으면 된다.
- 실행 방법, 클립 표, 채널 규칙 같은 세부 내용은 `README.md`에 있다. 이 파일은 맥락, 결정한 이유, 남은 일을 정리한다.

## 1. 목표와 현재 위치

Meshy로 만든 강아지 GLB를 받아, 사람 손이나 LLM 없이 Python(Blender)만으로 다음을 처리한다.

1. 표준 뼈대(33개)로 리깅
2. 공용 동작 클립 적용
3. GLB로 내보내기

계획서 `work-plan.md` 기준 진행 상황:

| 단계 | 상태 |
|---|---|
| A. 표준 뼈대 | 초안 완료 (`docs/skeleton-spec.md`, skeletonVersion 1) |
| B. 체형 맞춤 | 완료. 기준점 자동 추정 + JSON 수동 수정 경로 |
| C. 대기·걷기 | 완료 |
| D. Unity PC 재생 | **안 함**. 테스트 스크립트만 있음 (`unity/`) |
| E. 자동화 | `rig.py`, `run_all.ps1` 완료. 서버 API는 없음 |
| F. 나머지 행동 | 15개 클립 완료 (방향 전환, 앉기↔엎드리기 직접 전환은 없음) |

## 2. 환경

- Blender 5.2.3 LTS (Steam): `C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe`
- Python: 시스템 `python` (`tools/glb_info.py` 실행용)
- Blender MCP (`uvx blender-mcp`)가 Claude Code에 user 범위로 등록돼 있다.
  - Blender 사이드바(N) → BlenderMCP 탭 → Connect를 눌러야 연결된다(포트 9876).
  - 파이프라인 실행에는 MCP가 필요 없다. 결과를 눈으로 확인할 때만 쓴다.
- 이 폴더는 **git 저장소가 아니다**. 변경 이력이 없다.

## 3. 폴더

```
dog-rig/
  HANDOFF.md            이 파일
  README.md             실행법, 클립 표, 채널 규칙, 결과, 한계
  work-plan.md          원래 작업 계획서 (사용자 작성)
  run_all.ps1           input/*.glb 전부 처리 (-UseLandmarks: 저장된 기준점 사용)
  blender/
    rig.py              진입점. blender -b --factory-startup -P rig.py -- in.glb out.glb [옵션]
    estimate_landmarks.py, landmarks_from_blend.py   기준점 수정용
    poses/lie.json      사용자가 Blender에서 직접 잡은 엎드리기 포즈
    dogrig/             prepare, landmarks, skeleton, weights, animations, poses, validate, export
  docs/                 skeleton-spec.md, landmarks-format.md
  tools/glb_info.py     GLB 구조(뼈, 애니메이션 채널) 확인
  unity/                DogClipTester.cs + 확인 가이드 (실행 안 해봄)
  input/                m1_golden, m2_white, m3_cream (Meshy 원본)
  output/<모델>/        *_rigged.glb, landmarks.json, report.json, 검토용 .blend
```

원본 GLB 위치: `C:\Users\rksek\Documents\카카오톡 받은 파일\meshy_result+walk\meshy_result\`

## 4. 핵심 결정과 이유

| 결정 | 이유 |
|---|---|
| UniRig 같은 AI 리깅 대신 템플릿 + 측정 스크립트 | GPU가 필요 없고, 결과가 매번 같고, 표준 뼈대라 동작을 그대로 공유할 수 있음 |
| 가중치는 복셀 프록시에서 계산해 원본으로 전달 | Meshy 메시는 UV 경계마다 갈라져 있어서 Blender 자동 가중치(Bone Heat)가 원본에서 실패함 |
| 귀 가중치를 귀 주변으로 제한 (`weights.EAR_RULES`) | 늘어진 귀가 목에 붙어 있어 귀 뼈가 목을 끌고 가던 문제 |
| 모든 뼈의 로컬 X축 = 월드 +X (`skeleton._roll_target`) | +X 회전이 항상 "뼈 끝이 뒤/아래"가 되도록. 좌우 다리가 같은 부호로 굽음 |
| 모든 클립에 33개 회전 + 골반 위치만 내보냄 (`export.strip_channels`) | 상수 채널을 빼면 앉아 있기 같은 유지 자세가 서기로 돌아감. 스케일·위치를 빼야 뼈 길이를 덮어쓰지 않음 |
| 앉기·엎드리기는 모델마다 계산해 굽고 IK는 제거 | 바닥 접지가 모델 다리 길이에 따라 달라서. GLB에는 키프레임만 남음 |

## 5. 사용자가 확정한 동작 요구사항 (바꾸지 말 것)

대화 중 사용자가 직접 지정한 내용이다.

- **반기기(Greet)**: 몸통은 고정하고 꼬리 흔들기가 주 동작. 꼬리를 들고 흔든다(`TAIL_LIFT` 45°). 앞발 점프 버전은 하지 않기로 함.
- **꼬리 흔드는 동작 전부**: 꼬리를 들어서 잘 보이게.
- **머리 흔들림**: 처음 값의 50%로 줄임.
- **앉기**:
  - 앞다리 위팔·아래팔은 수직 일자로 편다.
  - 몸에 붙은 첫 마디(견갑골)만 굽어도 된다.
  - 엉덩이는 바닥에 닿고, 뒷발은 앞으로 온다.
  - 전환 속도는 처음의 2배(SitDown 18프레임, StandUp 15프레임).
- **엎드리기**: 사용자가 Blender에서 직접 잡은 포즈 `blender/poses/lie.json`을 쓴다. 이 결론이 나기까지 다음 요구가 반복됐다.
  - 마디끼리 겹치지 않을 것
  - 앞다리는 "_l" 모양으로 앞으로 뻗을 것
  - 뒷무릎은 옆이 아니라 뒤로 가서 바닥에 닿을 것
  - 발꿈치부터는 바닥을 따라 앞으로 뻗을 것

## 6. 알려진 문제

- `report.json` 경고:
  - 꼬리 털 늘어남(꼬리 흔드는 클립)
  - 앉아 있을 때 엉덩이가 바닥을 4~8cm 뚫음
  - SitDown, LieDown, GetUp 전환 중간에 바닥 뚫림
- m1, m3는 귀 끝 기준점이 머리 안쪽으로 잡혀서 늘어진 귀 끝이 덜 흔들린다.
- GLB 용량이 이전보다 약 1MB 늘었다. 채널 정리 후에도 쓰지 않는 데이터가 파일에 남아 있다.
- 세 모델의 체형이 비슷하다. 짧은 다리·긴 몸통 체형은 검증하지 않았다.
- 서기 → 엎드리기 전환은 각도를 그대로 보간하는 방식이라 발이 미끄러지거나 바닥을 뚫는다.
- **Blender에 열린 `output/m2_white/m2_white.blend`(사용자 세션)는 예전 상태다.** 저장하면 새로 만든 결과를 덮어쓴다. 다시 만들려면 `run_all.ps1 -UseLandmarks`.

## 7. 다음 할 일 (추천 순서)

1. **Unity PC 재생 확인 (단계 D)**
   - `unity/README.md`를 따라 15개 클립을 재생해 본다.
   - 확인할 것: 뼈 길이 유지, 클립 전환, 바닥 뚫림이 눈에 띄는지.
   - 테스트 스크립트의 키(1/2/Space)는 Idle·Walk만 다루니 필요하면 키를 늘린다.
2. **저장소 정리**: 원래 프로젝트 저장소(계획서의 `blender/`, `docs/` 구조)로 옮기거나 `git init`.
3. **관절 JSON 정식 형식(#17)** 받아서 `landmarks.load`를 맞춘다.
4. **서버 API**: 파일을 받아 `rig.py`를 실행하고 결과 GLB와 report를 돌려준다. 기존 작업자 실행 형식(`blender -b --factory-startup -P <script> -- <args>`)을 따른다.
5. 남은 동작: 방향 전환, 앉기↔엎드리기 직접 전환, 앉기도 손 포즈(`poses/sit.json`)로 바꿀지 사용자에게 확인.

## 8. 자주 쓰는 명령

모든 모델 다시 처리 (저장된 기준점 사용):
```powershell
C:\Users\rksek\Desktop\dog-rig\run_all.ps1 -UseLandmarks
```

GLB 구조 확인:
```powershell
python -I C:\Users\rksek\Desktop\dog-rig\tools\glb_info.py C:\Users\rksek\Desktop\dog-rig\output\m2_white\m2_white_rigged.glb
```

손 포즈를 새로 저장하려면 Blender MCP로 연결한 뒤 포즈 모드 리그에서 각 뼈의 `rotation_euler`(도)와 `pelvis.location`을 `lie.json`과 같은 형식으로 저장한다(README의 "손으로 잡은 포즈" 참고).
