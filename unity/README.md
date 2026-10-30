# Unity 개발 문서

Codex가 Unity 앱 전체를 맡아 이슈 단위로 구현하고, 사용자가 Unity 에디터와 Android 실기기에서 확인한다. 기능 검증을 먼저 진행하고 성능 측정·최적화는 마지막 단계에서 한다.

## 실제 프로젝트

- Unity Hub에서 열 경로: `unity/withme_frontend/` (저장소 루트 기준).
- 확인한 에디터 버전: **6000.0.76f1**. 실제 기준은 [ProjectVersion.txt](withme_frontend/ProjectSettings/ProjectVersion.txt).
- URP, Input System, uGUI, Test Framework 패키지는 현재 manifest에 포함되어 있다. 설치 상태는 기능 구현·실행 검증 완료를 뜻하지 않는다.
- Noto Sans KR 기본·동적 TMP 폰트와 검증 씬을 포함한다. glTFast 6.14.1로 샘플 로드·공용 걷기를 검증했다. Newtonsoft Json 3.2.1은 간접 의존성이다. #8 통신·로그인·세션, #9 등록·사진 업로드와 NativeGallery 1.9.5 연결을 구현했다. 모바일 갤러리 동작은 기기에서 확인해야 한다.
- 이 문서는 Unity 프로젝트 바깥인 `unity/`에 둔다. `Assets/`에 문서를 복사할 필요는 없다.

## 읽는 순서

| 문서 | 용도 |
|---|---|
| [AGENTS.md](AGENTS.md) | Codex 작업 규칙, 수정 범위, Git·에셋 관리 |
| [작업 흐름](docs/workflow.md) | 이슈 작성 양식, 작업 재개, 스킬 구성 |
| [작업 계획](docs/work-plan.md) | 구현 순서, 의존성, 진행 상태 |
| [모바일 구현·배포](docs/mobile-platform.md) | Unity 단독 Android 앱 구성, 화면·기기 기능·빌드·APK 배포 기준 |
| [통신·로그인 구현 기록](docs/issues/U03-issue-8-auth-plan.md) | Start 씬·실제/목 로그인·PC 검증·미완료 연동 |
| [등록·업로드 구현 기록](docs/issues/U04-issue-9-registration-upload-plan.md) | 이름·사진·정적 전송 안내·재시도·PC 검증·기기 미확인 항목 |
| [생성·복구 구현 기록](docs/issues/U06-issue-10-generation.md) | 사용자 제공 4면·GLB 목 서버, 생성 확인·진행·이어하기 |
| [연결 규격](docs/integration.md) | API·목 응답·GLB·뼈대 연결 기준 |
| [검증·인수](docs/verification.md) | 기본 검증, 실기기 확인, 최종 성능 검사 |

## 상위 요구사항

기능 범위는 [기능 명세](../docs/01-features.md), 기술 선택은 [아키텍처](../docs/02-architecture.md), 3D 처리는 [3D 파이프라인](../docs/03-3d-pipeline.md), API는 [API 문서](../docs/04-api-data.md)를 따른다. 이 문서들은 상위 PRD를 대체하지 않는다.

## 현재 준비 상태

#10은 사용자 제공 4면 사진과 정지 GLB를 로컬 목 HTTP 서버로 연결해 생성 확인·진행·사진 모드·재시작 복구를 PC에서 검증했다. Start 씬은 현재 목 로그인으로 설정했으며 코드 `123456`을 사용한다. 실제 생성·리깅·Android 확인은 별도다. [실행 방법과 검증 기록](docs/issues/U06-issue-10-generation.md).

2026-10-29 이슈 #6 초기 설정을 마쳤다. 별도 `SetupValidation.unity`에서 한글·동적 fallback·Input System 버튼 반응을 검증했다. [시작하기](../README.md#개발-시작하기-unity)와 [실행 기록](docs/issues/U01-issue-6-initial-setup.md)을 따른다.

이슈 #7은 사용자 제공 GLB로 런타임 로드·공용 Generic 걷기를 구현했다. `Assets/Scenes/CharacterPlaybackValidation.unity`에서 확인한다. glTFast는 `6.14.1`로 고정했으며 Android 모듈은 설치했으며 사용자 지시로 PC 모션 검증을 우선하고 APK는 보류했다. 대기 클립·기준 폰 확인·제품 기능은 완료되지 않았다. [#7 실행 기록](docs/issues/U02-issue-7-character-playback.md).

이슈 #8의 제품 시작 씬은 `Assets/Withme/Scenes/Start.unity`다. 와이어프레임 01·02, 통신·세션 갱신·오류·진입 상태 확인을 구현하고 PC 자동 검증을 통과했다. 기본값은 실제 연결이고 사용자 제공 publishable 키를 반영했다. 실제 초대 이메일 로그인·Android 저장소 동작·사용자 화면 확인은 남아 있다. 목 검증 방법은 [#8 기록](docs/issues/U03-issue-8-auth-plan.md)을 따른다.
