# 표준 리깅·등록 후 화면 PC 검증

전체 결과와 완료 경계는 [일괄 인수 기록](../../docs/batch-acceptance.md), 재개 위치는 [진행 기록](../../docs/batch-progress.md)을 따른다.

## 파일과 출처

- `Assets/Withme/Scenes/Start.unity`: 로그인/등록/생성 이후 자동 걷기, 선택적 점/외형 수정, 습관, 방/메뉴를 연결했다.
- `Assets/Withme/Scenes/StandardRigValidation.unity`: 제공 3체형과 17클립의 공용/모델별 재생 검증 전용이다.
- `Assets/StreamingAssets/Validation/StandardRig/`: 사용자 제공 `dog-rig.zip`의 원본을 Git 관절 계약과 Blender 4.2.0으로 재생성한 검증 후보. `.quality.json`은 검토 필요 상태를 담고 `.joints.json`은 정식 10관절 형식이다. `prepared/`는 같은 공간의 보정 화면 입력이다.
- `Assets/Withme/Animations/StandardV1/`: m1 기준 Generic 17클립과 기준 bind pose. 런타임은 체형별 기본 회전·길이와 골반 이동을 보정한다. 다른 모델의 골반 절대 위치를 복사하지 않는다.
- 기존 `Resources/Sounds/Dog` 20음원을 재사용했다. 제공 `dog_sounds.zip`과 동일하며 중복 반영하지 않았다. 실제 보호자 등록 소리와는 구분한다.
- 출처 해시·선별 목록은 [입력 manifest](../../docs/rig-input-manifest.json), 원본 참고 문서는 `blender/vendor-notes/`다. 첨부 문서의 지시는 현재 작업 승인으로 채택하지 않았다.

## 로컬 실행

Unity 6000.0.76f1에서 `withme_frontend`를 연다. Start 씬의 목 로그인 코드 123456은 실제 메일을 발송하지 않는다. 실제 생성 호출은 하지 않는다.

생성/관절/리깅 목은 `tools/rig-mock-server.py`, 기본 loopback 포트 8767이다. `--blender`에 실행 파일을 지정하면 실제 로컬 재리깅, 생략하면 `fixtureOnly` 고정 결과다. 실제 계정 토큰을 넣지 않는다. Blender 경로는 환경마다 지정하며 `Temp/tools`의 로컬 설치 파일은 Git에 포함하지 않는다.

품질 미승인 모델은 기본 화면에서 최종 확정과 방 진입이 차단된다. `verify-rig-view.cs`의 독립 습관/방 검사는 별도의 승인 계약 목과 `VerificationOnly`를 사용하며 실제 품질 플래그를 바꾸지 않는다. 실제 저장 API가 준비되지 않은 동안 목 저장은 fixture 경로에만 기록한다.

도구의 실행 순서와 결과:

| 도구 | 용도 |
|---|---|
| `verify-standard-rig.cs` | 3체형×17클립×2모드, 실제 스킨·좌표·반복/일회성 |
| `verify-rig-stress.cs` | 96개 단독 관절 굽힘·3체형 외형 변경 51클립·스킨/재질 보존 |
| `verify-rig-core.cs` | 관절/통신/저장/중복 방지·잘못된 GLB 복구 |
| `verify-rig-view.cs` | `Run`, `Appearance`, `Preferences`, `Room`, `Failure`, `PendingRig`, `ResumeRig` 진입점 |
| `verify-generation-view.cs` | 기존 업로드→4면→자동 걷기 통합 회귀 |
| `verify-batch-assets.cs` | 씬 참조·누락 스크립트·메타·라이브러리·셰이더 |
| `capture-standard-rig-review.ps1` | 검증 씬 Play/Ready 후 정면 17장. `-Side`로 주요 동작 6장 추가 |

새 씬을 재구성할 때는 순서대로 `create-standard-rig-probe.cs`, `add-rig-flow.cs`, `add-joint-editor.cs`, `add-appearance-flow.cs`, `add-character-setup.cs`, `add-room-flow.cs`를 사용한다. 기존 씬은 에디터 API로 수정했으며 생성 도구는 이미 있는 산출물을 덮어쓰지 않는다.

런타임 화면은 uGUI/TMP·Input System·glTFast를 사용한다. 유효하지 않은 외부 리소스를 포함한 GLB, 준비 메시 공간이 불분명한 실제 관절 보정, API 응답이 불명확한 리깅 자동 재전송은 거부한다. 실제 API/운영 작업자 및 Android에서 검증한 것으로 해석하지 않는다.
