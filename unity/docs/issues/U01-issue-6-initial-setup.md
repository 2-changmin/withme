# U01 / #6 — Unity 프로젝트 초기 설정 계획

- 확인일: 2026-10-29
- 이슈: [#6 — [황세인] Unity 프로젝트 초기 설정](https://github.com/2-changmin/withme/issues/6)
- 이슈 상태: OPEN. 이슈에 적힌 마감은 2026-10-28이며 현재 완료로 처리하지 않는다.
- 작업 브랜치: 사용자 지정 `unity`. 최신 `main` 병합 기준 `d4ba853`.
- 현재 상태: 사용자 실행 지시에 따라 Git 제외·LFS·TMP·Noto Sans KR·검증 씬 설정과 에디터 검증 완료. 초기 커밋·새 체크아웃 검증 진행 중.

## 이슈의 완료 조건

1. Unity 6 LTS 패치 버전 고정, URP, Input System, TextMeshPro + Noto Sans KR.
2. Unity `.gitignore`, Git LFS(`*.fbx *.glb *.png *.wav *.mp3`), Force Text.
3. `apps/unity`에 프로젝트 커밋.

## 경로 차이와 작업 범위

이슈 본문의 `apps/unity`와 달리 현재 프로젝트는 `unity/withme_frontend/`다. 현재 아키텍처 문서·Unity 지침·CLI/MCP 설정도 `unity/`를 기준으로 한다. 프로젝트 이동을 초기 설정에 섞지 않고 기존 경로를 유지하는 기준으로 계획한다. 추후 이슈 본문 또는 PR 설명에서 이 차이를 명시한다. GitHub 이슈 수정·댓글 전송은 이번 작업에서 수행하지 않는다.

이번 이슈는 재현 가능한 Unity 개발 환경을 저장소에 등록하는 작업이다. 로그인·서버 연동·GLB 로드·캐릭터 생성·방 상호작용 구현과 첫 APK는 후속 이슈로 둔다. 계획 작성 후 사용자가 실행을 지시해 설정·검증·초기 커밋까지 진행한다. 원격 푸시·이슈 종료는 이 작업에 포함하지 않는다.

## 현재 상태 — 파일 기준 확인

| 대상 | 확인한 사실 | 남은 검증 |
|---|---|---|
| Unity | `ProjectVersion.txt`: `6000.0.76f1` | 같은 버전으로 재개방·컴파일 |
| URP | `17.0.4`, Graphics·Quality 설정의 PC/Mobile GUID가 실제 에셋 `.meta`와 일치 | 현재 품질별 렌더링·누락 머티리얼 확인 |
| Input System | `1.19.0`, `activeInputHandler: 1`, 입력 액션 에셋 존재 | UI 클릭과 Android 터치 경로 확인 |
| TextMeshPro | uGUI `2.0.0`의 `Runtime/TMP` 포함 | TMP 기본 리소스 가져오기·설정 에셋 확인 |
| 한글 폰트 | Assets에서 Noto·TTF·OTF 파일 미발견 | Noto Sans KR·라이선스·TMP 폰트 생성·한글 가독성 확인 |
| 직렬화 | `m_SerializationMode: 2`(Force Text) | 에디터에서 설정 유지 확인 |
| 메타 파일 | `Visible Meta Files` | 새 에셋 임포트 후 `.meta` 누락 검사 |
| Git 제외 | 루트 `.gitignore`에 Unity 전용 규칙 없음. 프로젝트 `.gitignore` 없음 | 캐시·IDE·빌드 제외 규칙 작성·검증 |
| LFS | 로컬 git-lfs `3.7.1` 설치. 루트 `.gitattributes` 없음 | 확장자 규칙·필터 설정·포인터 검증 |
| Git 등록 | `git status --short`: `?? unity/` | 제외·LFS 설정 후 관리 대상만 초기 등록 |
| Android | IL2CPP, ARM64, 최소 API 23, 대상 API 자동 | 모듈·SDK·NDK·JDK·실기기 미검증 |
| 씬 | Build Settings에 `SampleScene.unity` 등록 | 기본 장면 실행 확인. 앱 화면은 아직 없음 |

파일에 설정이 있다는 사실과 실제 에디터·실기기 검증을 구분한다. 이전 [연결 검증](U01-editor-connection.md)은 에디터 통신의 근거이며 한글·터치·Android 통과 근거가 아니다.

## 실행 순서와 완료 기준

### 1. Git 등록 전 제외·LFS부터 정리

- 기존 사용자 파일을 보존하며 Unity 전용 `.gitignore`를 프로젝트 범위로 적용한다.
- `Library/`, `Temp/`, `Logs/`, `Obj/`, `UserSettings/`, `.vs/`, `.idea/`, `*.csproj`, `*.sln`, IDE 캐시·빌드 결과를 제외한다. `Assets/`, `.meta`, 패키지 잠금 파일은 유지한다.
- 루트 `.gitattributes`에 이슈 지정 바이너리 `*.fbx *.glb *.png *.wav *.mp3`의 LFS 규칙을 적용한다. `.meta`·C#·텍스트 씬·프리팹은 LFS로 보내지 않는다.
- 규칙은 **첫 add 전에** 적용한다. `git check-ignore`·`git check-attr`·등록 예정 목록으로 대상/제외가 맞는지 확인한다.
- 완료 조건: 생성 파일이 스테이징에 포함되지 않고, 지정 바이너리에 LFS 필터가 적용되며 실제 LFS 포인터 검사도 통과한다.

### 2. 기존 에디터·패키지 설정 확인

- `6000.0.76f1`을 유지하고 URP `17.0.4`, Input System `1.19.0`, uGUI `2.0.0` 기준으로 연다.
- Force Text·Visible Meta Files·새 Input System 설정을 에디터에서 확인한다. URP·렌더러 참조와 씬의 핑크 머티리얼·누락 참조를 확인한다.
- 기본 입력 액션을 재사용하고 새 UI 시스템이나 불필요한 패키지를 추가하지 않는다.
- 완료 조건: 해당 버전에서 컴파일 오류가 없고 기본 씬이 정상 표시된다.

### 3. TMP·Noto Sans KR 준비

- uGUI에 TMP가 포함돼 있으므로 별도 구형 `com.unity.textmeshpro` 패키지를 중복 설치하지 않는다.
- TMP 기본 리소스 필요 여부를 확인하고, Noto Sans KR 원본·라이선스·TMP 폰트 에셋을 프로젝트 안에서 관리한다.
- 기본 폰트·fallback을 정하고 사용자 문구·반려견 이름에 필요한 한글·숫자·기호를 검증한다. 전체 한글을 무조건 대형 정적 아틀라스로 만드는 방식은 피한다.
- 검증용 별도 장면에서 실제 와이어프레임 문구와 한글 이름이 네모 글자 없이 표시되는지 확인한다. 사용자 화면 구현은 이 단계에 포함하지 않는다.
- 완료 조건: 필요한 한글 표시가 정상이고 폰트·설정·라이선스·`.meta`가 함께 관리된다.

### 4. 최소 실행 검증

- 에디터 재개방 후 기본 씬과 한글 표시를 확인한다.
- 기본 UI 클릭과 Input System 입력을 검증한다. Android 모듈 준비 여부를 기록하고, 미준비 시 터치·기기 실행을 통과로 적지 않는다.
- 설정 변경만을 검증하는 형식적인 단위 테스트는 만들지 않는다. 실제 컴파일·씬 표시·한글·입력·Git 제외 검증을 증거로 남긴다.
- 완료 조건: 새 컴파일 오류·누락 참조 없이 해당 검증이 통과한다. Android APK·GLB 재생은 U02에서 검증한다.

### 5. 초기 프로젝트 등록과 재현 문서 확정

- `Assets/` + `.meta`, `Packages/`, `ProjectSettings/`, 필요한 문서·라이선스만 등록한다.
- `git diff --cached --stat`, 생성 파일 포함 여부, LFS 추적·포인터 상태를 확인한다. 현재 `unity/` 전체가 미추적이므로 무검토 일괄 add를 하지 않는다.
- 메인 README의 사양·시작하기·현재 상태를 최종 설정에 맞춰 갱신한다.
- 커밋·푸시는 구현 단계의 사용자 위임 범위에 맞춰 진행하고, #6의 저장소 등록 완료는 실제 커밋 근거가 생긴 뒤 표시한다.
- 완료 조건: 동일 에디터를 쓰는 새 체크아웃에서 LFS 에셋을 받고 프로젝트를 열 수 있다. 재현을 실행하지 못했다면 미실행으로 남긴다.

## 이번에 작성한 문서

- 루트 README: Unity 사양, 준비 도구, 정확한 프로젝트 경로, 최초 실행, 현재 미구현 상태, 서버 연동 시 주의점.
- 작업 계획: U01을 실제 이슈 #6과 연결하고 다음 실행 순서를 명시.
- 본 문서: 완료 조건별 현재 근거·미완료 항목·경로 차이·검증 기준.

## 구현 시작 시 확인점

- Noto Sans KR 원본과 라이선스 확보·프로젝트 폰트 경로.
- 같은 버전 Android Build Support 모듈 설치 여부와 기준 기기.
- 첫 Git 등록에서 예상 바이너리 용량·LFS 대상 목록.

선행 입력 없이 진행할 수 있는 Git 제외·LFS·에디터 설정부터 처리하고, 실제 폰트·입력·재현 검증까지 끝난 뒤 이슈 완료 여부를 판단한다.

## 실행 결과 — 2026-10-29

- 프로젝트 범위 `.gitignore`로 Unity 캐시·로그·IDE·빌드 결과를 제외했다.
- 루트 `.gitattributes`에 이슈 지정 fbx/glb/png/wav/mp3와 원본 폰트 otf/ttf의 LFS 규칙을 추가했다. 씬·프리팹·asset·meta는 텍스트로 유지한다. 저장소 로컬 LFS 훅·필터를 활성화했다.
- Unity `6000.0.76f1`, URP `17.0.4`, Input System `1.19.0`, uGUI `2.0.0` 유지. Force Text·Visible Meta Files를 실제 에디터에서 확인했다.
- TMP Essential Resources만 가져왔다. Noto 공식 저장소의 Noto Sans KR Regular 원본(4,644,748바이트)·OFL·출처·SHA-256을 저장했다.
- 기본 정적 폰트는 와이어프레임 문구·Latin·검증 이름 378자, 동적 fallback은 추가 한글용이다. 두 아틀라스는 1024, sampling 48 / padding 5 / scale 1. 동적 폰트는 빌드 시 캐시 지우기를 활성화하고 저장 전 글자 캐시를 비웠다.
- 기본 TMP 폰트를 Noto Sans KR로 지정하고 별도 `SetupValidation.unity`를 생성했다. 제품 UI·SampleScene·기본 Build Settings의 씬 목록은 변경하지 않았다.
- 반복 가능한 초기 생성 도구를 `unity/tools/initialize-fonts.cs`에 두었다. `script run`으로 실행하며 런타임 빌드에 포함되지 않는다. 최초 도구 호출은 eval_file의 using 지원 차이로 컴파일 거부됐으며, 정식 C# 컴파일 방식으로 전환 후 diagnostics `[]`로 성공했다.

| 검증 | 실제 결과 |
|---|---|
| 에디터 연결·설정 | ready, `6000.0.76f1`, ForceText, Visible Meta Files |
| TMP·한글 | 기본 378자 생성 성공, 390 × 844 Game View에서 한글·이름·제품 문구 확인 |
| 동적 fallback | 기본 목록에 없는 ‘똠’이 동적 폰트로 표시됨을 `textInfo.characterInfo.fontAsset`으로 확인 |
| 누락 스크립트 | 검증 씬 0개 |
| Play Mode | frameCount 6037 → 6064, `job wait`: met=true, framesObserved=2 |
| UI 입력 | 임시 Input System 마우스의 누름·뗌 → InputSystemUIInputModule → Button → TMP 문구 ‘입력 확인 완료’ 확인 |
| 자동화 입력 조건 | 에디터 포커스가 없어 기본 경로는 입력을 무시했음. 런타임 임시 설정 객체에서 IgnoreFocus·AllDeviceInputAlwaysGoesToGameView로 분리 검증한 뒤 원본 복원·임시 장치 제거. 프로젝트 설정에는 저장하지 않음 |
| 최종 콘솔 | 오류 조회 결과 `[]` |
| 생성 에셋 | 폰트·씬·리소스 `.meta` 누락 없음. 임시 캡처는 Assets에서 제거 |
| Android | 로컬 설치 엔진에 Windows 지원만 있음. Android 모듈·APK·실기기 터치는 미실행 |

검증 화면은 이 PC의 Codex 시각화 폴더 `unity-setup-passed.png`에 보관했다. 스크린샷만으로 동작 통과를 판정하지 않고 프레임·입력 결과를 함께 확인했다. 사용자 직접 시각·조작 확인은 미확인이다.
