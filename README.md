# 곁에 — 반려견 추억 3D 앱 PRD

> 보호자의 사진으로 그 아이를 닮은 3D 캐릭터를 만들고, 함께 놀며 그리움을 달래는 작은 추모 공간
> 작성일 2026-10-27 · 개발자 5인 · 개발 기간 10/27 ~ 11/08 · 비공개 데모 (Android)

## 한 줄 정의

보호자가 반려견 사진을 올리면 **AI가 서 있는 모습의 4면 이미지를 만들고 → Meshy가 3D 모델로 만들고 → 공용 뼈대에 자동 리깅**해서, 쓰다듬고 공을 던지며 함께 지낼 수 있는 캐릭터로 만든다.

캐릭터는 사진을 바탕으로 AI가 만든 표현이며, 생전의 의식이나 성격을 복원한 존재로 설명하지 않는다.

## 목표

- 보호자 사진 1~4장으로 **그 아이를 닮은 3D 캐릭터**가 만들어지고, 공용 애니메이션 세트로 움직인다.
- 생성이 실패해도 보호자는 사진 모드와 추억 앨범으로 끝까지 이용할 수 있다.
- 11/08까지 Android 빌드 1개로 최대 10명 파일럿을 진행한다.

## 비목표

- 실사 수준의 털 표현, AR, 자체 3D 생성 모델 학습
- 짖는 소리 합성·복제, 자유 대화, 감정 추론
- 배고픔·청결·출석·친밀도 같은 돌봄 의무
- 결제, 공개 커뮤니티, 고양이 등 다른 동물
- 생성 품질을 수치로 보여 주는 "닮음 점수"

## 대상 사용자

반려견을 떠나보낸 **초대받은 성인 보호자**, 계정당 반려견 1마리.

| 문제 | 지금의 대안 | 곁에가 주는 것 |
|---|---|---|
| 사진·영상은 보기만 할 수 있고 함께하는 감각이 없다 | 앨범, SNS | 쓰다듬고 공을 던지는 상호작용 |
| 범용 강아지 캐릭터는 "우리 아이"가 아니다 | 게임 속 강아지 | 사진으로 만든 개별 3D 모습 + 보호자 수정 |
| 실제 짖는 소리가 흩어져 있다 | 영상 속에만 남아 있음 | 녹음을 잘라 행동에 연결 |
| 돌봄 게임은 의무와 죄책감을 준다 | 닌텐독스 류 | 돌봄 의무 없음 |

**설계 조건**: 보호자 사진은 대부분 앉거나 엎드린 사진이다. 그래서 **앉은 사진만 있어도 제작이 되어야 하고**, 관절 보정 같은 작업은 "코 끝을 눌러 주세요" 수준의 안내로 끝나야 한다.

## 문서 목록

| 문서 | 내용 |
|---|---|
| [01-features.md](docs/01-features.md) | MVP 기능 범위, 사용자 흐름 |
| [02-architecture.md](docs/02-architecture.md) | 시스템 구조, 기술 스택 |
| [03-3d-pipeline.md](docs/03-3d-pipeline.md) | 4면 이미지 생성 → Meshy → 관절 → Blender 리깅 → 애니메이션 |
| [04-api-data.md](docs/04-api-data.md) | 백엔드 API, 테이블, 캐릭터 데이터 형식 |
| [05-roles.md](docs/05-roles.md) | 5인 역할 분담, 담당자별 작업 목록, 연결 지점 |
| [06-schedule.md](docs/06-schedule.md) | 일정, 10/30 관문, 일정이 밀릴 때 축소 순서 |
| [07-cost-risk.md](docs/07-cost-risk.md) | 비용 예산, 리스크와 대응 |
| [08-quality-ethics.md](docs/08-quality-ethics.md) | 성공 지표, 인수 테스트, 윤리·개인정보, 열린 질문 |
| [auth-for-unity.md](docs/auth-for-unity.md) | Unity 로그인 연동: Supabase 인증 코드 요청·검증·갱신 순서, 오류 코드 |
| [Unity 작업 계획](unity/docs/work-plan.md) | 초기 설정·화면·연동·검증 순서 |
| [모바일 구현·배포 명세](unity/docs/mobile-platform.md) | Unity 단독 Android 앱, 모바일 대응·빌드·APK 배포 기준 |
| [이슈 #6 실행 계획](unity/docs/issues/U01-issue-6-initial-setup.md) | 현재 설정 점검 결과와 초기 설정 완료 조건 |
| [UI 와이어프레임](unity/docs/wireframe/README.md) | 화면 구조·문구·등록 후 3D 메인과 통합 메뉴 |

## 반드시 지킬 제품 원칙

- 접속하지 않아도 캐릭터의 상태가 나빠지지 않는다. 배고픔·청결·출석·친밀도 없음
- AI가 반려견의 감정·사후 상태를 안다고 표현하지 않는다
- 보호자가 제공하지 않은 사건을 실제 추억처럼 만들지 않는다
- 원본 기록 / 원본에서 편집한 소리 / AI가 만든 캐릭터를 화면에서 항상 구분한다
- 강아지 소리는 보호자가 등록한 실제 녹음만 쓴다
- 푸시·기념일 알림을 쓰지 않는다
- 캐릭터 없이 앨범만 쓰는 경로를 같은 수준으로 제공한다
- 작업이 성공하지 않았는데 성공·완료로 표시하지 않는다

| 상황 | 사용할 문구 | 피할 문구 |
|---|---|---|
| 제작 안내 | 사진을 바탕으로 AI가 만든 캐릭터예요 | 원래 아이를 완벽히 복원했어요 |
| 재방문 | 오늘은 어떤 기억을 꺼내볼까요? | 오래 안 와서 많이 외로웠어요 |
| 오류 | 잠시 불러오지 못했어요. 사진은 그대로 있어요 | 아이를 잃어버렸어요 |

금지 단어: '부활', '되살렸어요', '진짜 마음을 알아냈어요', '닮음 점수 99%'

## 개발 시작하기 (백엔드)

필요한 것: Python 3.12 이상, Docker Desktop

```bash
# 1. 로컬 DB 띄우기 (supabase/migrations가 자동 적용됨)
docker compose up -d

# 2. 백엔드 설치
cd backend
python3 -m venv .venv
source .venv/bin/activate
pip install -e ".[dev]"
cp .env.example .env

# 3. API 서버 → http://localhost:8000/docs 에서 API 문서 확인
uvicorn app.api.main:app --reload

# 4. 작업자 (다른 터미널)
python -m app.worker.main

# 5. 테스트·린트
pytest
ruff check app tests
```

로컬에서는 `AUTH_DEV_BYPASS=true`라서 `X-Dev-User: <아무 UUID>` 헤더로 로그인 없이 API를 호출할 수 있다. 캐릭터 API는 지금 목 응답을 돌려준다 (`MOCK_CHARACTER=true`).

**외부 API 비용**: 유료 API를 부르는 작업은 `MOCK_서비스명` 스위치를 두고 기본값을 `true`(호출 안 함)로 한다. 자기 담당 부분을 실제로 테스트할 때만 자기 `.env`에서 `false`로 바꾼다. 지금은 `MOCK_MESHY`가 있다.

**작업자 처리 함수 담당**: `backend/app/worker/handlers/`의 파일마다 담당자와 이슈 번호가 적혀 있다. 자기 파일의 `handle()`만 구현하면 큐와 재시도는 공통 코드가 처리한다.

## Unity 사양과 현재 상태

실제 프로젝트는 **`unity/withme_frontend/`**다. Unity Hub에는 저장소 루트나 `unity/`가 아니라 이 폴더를 추가한다. 이슈 #6의 초기 `apps/unity` 표기는 기존 저장소 구조에 맞춰 `unity/withme_frontend/`로 정정했다.

| 항목 | 사양 | 현재 상태 |
|---|---|---|
| 에디터 | Unity **6000.0.76f1** / Unity 6.0 LTS | `ProjectVersion.txt`에 고정. 같은 패치 버전 사용 |
| 렌더링 | URP **17.0.4** | 패키지 및 PC·Mobile 렌더 파이프라인 에셋 참조 확인 |
| 언어·UI | C#, uGUI **2.0.0** + TextMeshPro | TMP 기본 리소스 포함. 검증 씬에서 한글 표시 확인 |
| 한글 폰트 | Noto Sans KR Regular | 원본·OFL 라이선스·기본 378자 정적 폰트·동적 fallback 포함. 아틀라스 각각 1024 |
| 입력 | Input System **1.19.0** | 새 Input System + UI 입력 모듈. 에디터에서 포인터 이벤트로 버튼 반응 검증. 실기기 터치는 미검증 |
| 에셋 관리 | Force Text, Visible Meta Files, Git LFS | 에디터 설정 확인. 생성 폴더 제외·바이너리 LFS 규칙 적용 |
| Android | IL2CPP, ARM64, 최소 API 23 / 대상 API 자동 | Android Build Support·SDK·NDK·JDK 설치 완료. PC 걷기 검증 완료, APK·실기기 검증은 보류 |
| 테스트 | Unity Test Framework **1.6.0** | 패키지 설치됨. 기능 테스트 미실행 |
| 에디터 연결 | Unity 공식 플러그인 + Pipeline **0.9.1-exp.1** | CLI·MCP 연결 검증 기록 있음. 일반 실행에는 CLI·MCP 불필요 |
| GLB·통신·인증 | glTFast **6.14.1**, `UnityWebRequest`, Newtonsoft Json, Supabase Auth | #7 샘플 걷기 및 #8 통신·세션·로그인 UI의 PC 목 검증 완료. 실제 OTP 로그인·기기 저장은 미검증. Newtonsoft Json 3.2.1은 간접 의존성 |

현재는 **샘플 재생·로그인·사진 등록 구현 단계**다. #8 이메일 코드 화면·통신·세션에 이어 #9 이름·사진 선택·정적 전송 안내·업로드·재시도를 구현하고 PC에서 검증했다. 사진 선택은 NativeGallery **1.9.5**를 사용한다. 실제 이메일 OTP·실계정 업로드·Android 갤러리와 보안 저장소는 미검증이며, 강아지 생성·상호작용 연결은 후속 단계다. 위 표의 에디터 검증은 Android 빌드·실기기 통과를 뜻하지 않는다. [#9 구현·검증 기록](unity/docs/issues/U04-issue-9-registration-upload-plan.md).

초기 프로젝트는 `unity` 브랜치의 `b22441c1`에 등록했다. 새 체크아웃의 LFS 복원·컴파일·폰트·씬 참조를 검증했으며, 최초 임포트 시간 초과와 CLI 배치 명령 우회 방법은 [이슈 #6 실행 기록](unity/docs/issues/U01-issue-6-initial-setup.md)에 남겼다.

사용자 제공 강아지 샘플의 런타임 로드·공용/내장 걷기 비교는 `Assets/Scenes/CharacterPlaybackValidation.unity`에서 확인한다. 대기 클립과 기준 폰 재생·FPS는 미확인이며 [#7 실행 기록](unity/docs/issues/U02-issue-7-character-playback.md)을 따른다.

## 개발 시작하기 (Unity)

필요한 것: Git, Git LFS, Unity Hub, **Unity Editor 6000.0.76f1**. Android 빌드에는 같은 에디터의 **Android Build Support / Android SDK & NDK Tools / OpenJDK** 모듈이 필요하다.

1. Unity 프로젝트가 포함된 브랜치를 받은 뒤 저장소 루트에서 LFS를 준비한다. 처음 설치한 PC에서는 `git lfs install`을 한 번 실행하고 `git lfs pull`로 폰트·이미지·모델·음원을 받는다. 원본 폰트 대신 LFS 포인터 텍스트만 있으면 Unity가 폰트를 읽을 수 없다.

   ```bash
   git lfs install
   git lfs pull
   ```

2. Unity Hub에서 **Add → Add project from disk**로 `unity/withme_frontend/`를 추가하고 **6000.0.76f1**로 연다. 다른 에디터 버전으로 자동 업그레이드하지 않는다.
3. 첫 실행의 패키지 다운로드·임포트·컴파일이 끝날 때까지 기다린다. Console에 컴파일 오류가 없는지 확인한다. 패키지 기준은 `Packages/manifest.json`과 `Packages/packages-lock.json`이다.
4. 제품 시작 장면은 **`Assets/Withme/Scenes/Start.unity`**다. Game View를 세로형 **390 × 844**로 맞추고 Play한다. 제공된 publishable 키를 연결 설정에 반영했으며, 실제 로그인은 초대된 이메일로 사용자가 요청·수신한 코드로 확인한다. 메일 없이 UI를 확인하려면 Play 종료 후 LoginFlow의 `useDemoResponses`를 켜고 예제 이메일·코드 `123456`을 사용한다. 이 모드는 목 검증이며 실제 로그인이 아니다. PC는 메모리 저장소라 Play 종료 시 로그인이 초기화된다. [#8 실행 기록](unity/docs/issues/U03-issue-8-auth-plan.md)을 따른다.

   기존 SampleScene과 검증 씬은 유지했다. 한글·입력만 확인하려면 `Assets/Scenes/SetupValidation.unity`에서 ‘입력 확인’ 버튼을 눌러 ‘입력 확인 완료’로 바뀌는지 확인한다. 클릭할 때는 Game View에 포커스를 둔다.
5. TMP 기본 리소스·폰트 설정은 프로젝트에 포함돼 있어 새 PC에서 다시 생성하지 않는다. 기본 폰트는 `Assets/Fonts/NotoSansKR/NotoSansKR-UI.asset`, 사용자 이름용 보조 폰트는 `NotoSansKR-Dynamic.asset`이다. 폰트 출처·라이선스는 [에셋 출처](docs/asset-sources.md), UI 설계는 [전체 SVG](unity/docs/wireframe/00-overview.svg)를 확인한다.

Unity 화면 작업에는 백엔드 실행이 필수는 아니다. 실제 API 연동을 시작할 때는 위 백엔드 절차로 서버를 실행하고 [연결 기준](unity/docs/integration.md)과 [인증 문서](docs/auth-for-unity.md)를 따른다. Android 실기기에서 PC API에 접근할 때는 PC의 LAN 주소나 `adb reverse`를 사용한다. 기기 자체의 `localhost`를 PC 주소로 사용하지 않는다.

등록·확정이 끝난 사용자는 앱을 열 때 강아지가 있는 **3D 메인**으로 진입하고, 상호작용 외 기능은 **통합 메뉴**로 제공하는 방향으로 설계했다. 현재 와이어프레임에서는 추억 앨범을 제외했으며, 이는 상위 PRD 전체에서 앨범 기능을 삭제했다는 뜻은 아니다.

커밋 대상은 `Assets/`와 `.meta`, `Packages/`, `ProjectSettings/`, Unity 작업 문서·설정 도구다. `Library/`, `Temp/`, `Logs/`, `Obj/`, `UserSettings/`, IDE 생성 파일·빌드 결과는 프로젝트의 `.gitignore`에서 제외한다. `.gitattributes`는 `fbx/glb/png/wav/mp3/otf/ttf`를 LFS로 관리한다. 씬·프리팹·`.asset`·`.meta`는 텍스트로 유지하며, 커밋 전 등록 대상과 LFS 상태를 확인한다.
