# U02 / #7 — GLB 런타임 로드·공용 걷기 검증

- 이슈: [#7](https://github.com/2-changmin/withme/issues/7), 마감 2026-10-30.
- 작업일: 2026-10-29. 브랜치: 사용자 지정 `unity`.
- 상태: 에디터 구현·통합 검증 완료, PC 걷기 재검증·재생 캡처 완료. APK는 사용자 지시로 보류했으며 대기 클립·기준 폰 검증은 미완료.
- 기존 모바일 명세·문서 변경을 보존했다. 제품 UI·서버 인증·생성 API·표준 뼈대 확정은 이번 변경 범위에 포함하지 않는다.

## 입력과 범위

사용자가 제공한 `meshy_result+walk.zip`의 `01a18145_rigged_walk.glb`를 사용했다. 다른 원본 GLB 3개는 뼈대·애니메이션이 없어 가져오지 않았다.

- GLB 2.0, 스킨 1개, 뼈 25개, 메시 정점 45,088개, 삼각형 31,173개.
- `Walk` 1개, 길이 약 1.041667초. 회전·위치·스케일 채널을 포함한다. 대기 클립 없음.
- SHA-256: `58c96215009bd871cce4205ce12e74b06a68794ace3d9aa150c86b6694256aba`.
- 샘플 뼈 계층은 `Dog_Rig/root/...`이며 표준 규격을 대신하지 않는다. 귀·턱 등 상위 계획과 차이가 있으므로 다른 체형·표준 뼈대와의 호환은 미확인이다.
- 제공된 한 샘플에서 추출한 걷기를 공용 에셋으로 분리해 연결했다. 여러 강아지 간 공용화·리타기팅 성공을 뜻하지 않는다.

## 구현

- Unity Package Manager 조회에서 권장된 `com.unity.cloud.gltfast@6.14.1`을 설치·고정했다. 프로젝트 에디터 `6000.0.76f1`은 유지했다.
- `Assets/Scenes/CharacterPlaybackValidation.unity`: 카메라·조명·바닥·uGUI 검증 UI·Input System EventSystem. 세로 `390 × 844`와 안전 영역 대응.
- `Assets/Validation/Character/CharacterPlaybackProbe.cs`: 비동기 런타임 로드, 25개 클립 경로·스킨 검사, 공용/내장 걷기 전환, 기본 자세 복원, 오류·재시도·중복 요청 방지·종료 정리, 일시 중단·복귀 시 재생/측정 처리.
- `Assets/Validation/Character/Animation/SampleSharedWalk.anim`: 에디터에서 추출한 non-legacy 걷기. `SampleWalk.controller`를 런타임에 받은 뼈대에 연결한다. Root Motion은 끄고 제자리 걷기를 확인한다.
- `Assets/Validation/Character/Source/DogWalk.glb`: 클립 추출·편집용 원본. `Assets/StreamingAssets/Validation/DogWalk.glb`: 실행 중 직접 읽는 동일 바이트. StreamingAssets는 에디터 임포트를 생략하므로 두 경로를 분리했다. 두 파일은 같은 LFS 객체로 관리할 수 있다.
- `Assets/Resources/CharacterProbe/`: 샘플 재질 조합을 빌드에서 유지하는 참조. 다른 재질·셰이더 조합은 별도 검증해야 한다.
- 기본 자세 버튼은 정적 bind pose이며 대기 애니메이션으로 표시하지 않는다. FPS는 로드 후 1초를 제외하고 모드 전환·복귀마다 새로 집계한다. 에디터 수치를 폰 수치로 취급하지 않는다.

## 에디터 검증 결과

실제 Play Mode 프레임 진행을 `job wait`으로 확인했다(`met=true`, `framesObserved=2`).

`unity/tools/verify-character-probe.cs` 결과:

| 항목 | 결과 |
|---|---|
| 런타임 로드·경로 검사 | 성공, 클립 대상 뼈 경로 25개 일치 |
| 공용 Animator 걷기 | Walk 상태에서 넓적다리 회전 변화 38.15도, 스킨 정점 변위 최대 약 0.485 Unity 단위 |
| 내장 걷기 | 별도 Animation 재생 중 넓적다리 회전 변화 22.01도 |
| 같은 시점 비교 | 정규화 시각 0.4에서 넓적다리 회전 차이 0도 |
| 기본 자세 | 정지 후 프레임 경과에도 회전 유지 |
| 파일 없음 | 실패 안내, 준비 완료로 처리하지 않음 |
| 뼈 경로 불일치 | 실패 안내, 기존 후보 리소스 정리 |
| 재시도·중복 로드 | 복구 성공, 동시 요청 추가 실행 없음, ModelAnchor 자식 1개 유지 |
| UI 연결 | 재생 버튼 3개 각각 persistent listener 1개, 실제 이벤트 호출로 동작 검증 |
| 콘솔 | 오류 0, 경고 0 |

세로 Game View를 캡처해 모델·한글·버튼 배치를 확인했다. 자동 검증은 사용자 직접 조작·걷기 자연스러움 판정이나 실제 Android 동작을 대신하지 않는다. 포커스 없이 Play Mode 검증할 때만 Run In Background를 켰고 이후 원복했다.

## 재현

1. 같은 에디터로 프로젝트를 열고 LFS를 복원한다.
2. `Assets/Scenes/CharacterPlaybackValidation.unity`를 연다.
3. Game View를 390 × 844로 맞추고 Play. 첫 실행에서 런타임 GLB 로드 후 공용 걷기가 재생된다.
4. 내장 걷기·공용 걷기·기본 자세·다시 불러오기를 비교한다. 대기 애니메이션은 없다.
5. 자동 검증은 Play 중 아래를 실행한다. 에디터 자동 tick과 포커스 조건은 [CLI 검증 기록](U01-issue-6-initial-setup.md)을 참고한다.

```powershell
unity command script run --caller plugin --skill unity-cli --project-path ./unity/withme_frontend --timeout 90 -- --file ../tools/verify-character-probe.cs --entry VerifyCharacterProbe.Run --timeout 75
```

최초 씬 생성 도구 `create-character-probe.cs`는 기존 검증 씬을 덮어쓰지 않는다. `select-validation-view.cs`는 에디터 Game View만 세로로 설정하며 제품 Player Settings를 바꾸지 않는다.

## 남은 완료 조건

- 대기 클립 확보 또는 별도 제작 후 공용 Animator 상태 추가·실제 움직임 확인. 임의로 만든 정지 자세로 대기 루프를 대체하지 않는다.
- Android APK 생성·설치·재생·기준 폰 FPS 측정. 연결 기기는 아직 확인되지 않았다.
- 다른 체형으로 재사용하려면 표준 뼈 이름·계층·로컬 축·기본 자세를 확정하고 샘플을 추가 검증한다.
- 이슈 #7은 위 완료 조건이 충족되기 전 종료하지 않는다.

## 실행 중 확인한 도구 차이

- 현재 Pipeline 0.9.1에는 package 명령 그룹이 없어 Unity Package Manager API로 검색·설치했다. manifest를 직접 수정하지 않았다.
- 패키지 설치 후 컴파일·임포트에 시간이 걸렸다. ready 상태를 확인한 뒤 실행했다.
- StreamingAssets의 GLB에는 ScriptedImporter 필드가 없어 최초 생성 도구가 중단됐다. 편집 원본과 실행용 바이트를 분리한 뒤 성공했다.
- 스크린 캡처 크기만 세로로 지정하면 기존 가로 Game View가 늘어나는 도구 동작을 확인했다. Game View 자체를 세로로 설정한 뒤 다시 캡처했다.

## PC 걷기 우선 검증 — 2026-10-30

- 사용자 지시: 일반 수정은 PC 에디터 검증을 먼저 반복하고 APK는 기기 기능·주요 단계에서만 만든다. 현재 APK 작업은 보류했다.
- Android 모듈·SDK·NDK·JDK 설치는 사용자 라이선스 동의 후 완료했다. OpenJDK 17.0.18+8, NDK r27c(27.2.12479018), SDK platform 34·35·36, build-tools 36.0.0.
- 첫 Android 빌드는 실행 중 추가한 모듈의 postprocess 단계에서 `Build target 'Android' not supported` 오류가 발생했다. 보고서의 `Succeeded`와 오류 2개가 모순됐고 APK가 없음을 확인했으므로 성공으로 처리하지 않는다. 로그는 무시되는 Logs/issue7-build-first.log와 issue7-build-first-status.json에 보존했다.
- 임시 식별자·화면 방향·서명 설정을 원복하고 에디터를 재시작했다. 두 번째 APK 빌드는 실행하지 않았다.
- 재시작 후 PC 에디터 Play Mode에서 런타임 로드·공용 걷기·내장 걷기·실패/재시도 검증을 다시 통과했다. 넓적다리 회전 변화 35.56도, 스킨 변위 약 0.468, 동일 시점 공용/내장 회전 차이 0도, 콘솔 오류·경고 0개.
- 실제 재생 중 프레임 13,769 → 13,978 구간 약 1.08초를 20장으로 캡처했다. 세로 Game View의 제자리 걷기 루프를 확인했으며 캡처 GIF는 로컬 Codex 시각화 폴더의 character-walk.gif에 보관했다.
- 사용자 확인용 검증 씬을 Play 상태로 열어 두었다. 임시 Run In Background override는 원복한다. 화면에서 공용 걷기·내장 걷기·기본 자세를 비교할 수 있다.
- 자동 변형·재생 통과와 걷기 자연스러움에 대한 사용자 판정은 구분한다. 대기 동작·다른 체형 호환·Android 기기 FPS는 여전히 미확인이다.

## 사용자 확인 — 2026-10-30

- 사용자가 Unity PC 걷기 검증 결과를 직접 확인하고 커밋·푸시를 지시했다.
- PC 에디터의 런타임 로드·걷기 재생은 사용자 확인 완료로 기록한다. APK 테스트·Android 실기기 FPS는 수행하지 않았다.
- 사용자 지시에 따라 #7에는 PC 검증 결과를 댓글로 남기고 이슈는 OPEN으로 유지한다. 대기 클립 등 남은 완료 조건은 완료 처리하지 않는다.
