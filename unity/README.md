# Unity 개발 문서

Codex가 Unity 앱 전체를 맡아 이슈 단위로 구현하고, 사용자가 Unity 에디터와 Android 실기기에서 확인한다. 기능 검증을 먼저 진행하고 성능 측정·최적화는 마지막 단계에서 한다.

## 실제 프로젝트

- Unity Hub에서 열 경로: `unity/withme_frontend/` (저장소 루트 기준).
- 확인한 에디터 버전: **6000.0.76f1**. 실제 기준은 [ProjectVersion.txt](withme_frontend/ProjectSettings/ProjectVersion.txt).
- URP, Input System, uGUI, Test Framework 패키지는 현재 manifest에 포함되어 있다. 설치 상태는 기능 구현·실행 검증 완료를 뜻하지 않는다.
- Noto Sans KR 기본·동적 TMP 폰트와 검증 씬을 포함한다. Newtonsoft Json 3.2.1은 현재 간접 의존성이다. glTFast·모바일 파일 선택·앱 통신 구현은 후속 작업이다.
- 이 문서는 Unity 프로젝트 바깥인 `unity/`에 둔다. `Assets/`에 문서를 복사할 필요는 없다.

## 읽는 순서

| 문서 | 용도 |
|---|---|
| [AGENTS.md](AGENTS.md) | Codex 작업 규칙, 수정 범위, Git·에셋 관리 |
| [작업 흐름](docs/workflow.md) | 이슈 작성 양식, 작업 재개, 스킬 구성 |
| [작업 계획](docs/work-plan.md) | 구현 순서, 의존성, 진행 상태 |
| [연결 규격](docs/integration.md) | API·목 응답·GLB·뼈대 연결 기준 |
| [검증·인수](docs/verification.md) | 기본 검증, 실기기 확인, 최종 성능 검사 |

## 상위 요구사항

기능 범위는 [기능 명세](../docs/01-features.md), 기술 선택은 [아키텍처](../docs/02-architecture.md), 3D 처리는 [3D 파이프라인](../docs/03-3d-pipeline.md), API는 [API 문서](../docs/04-api-data.md)를 따른다. 이 문서들은 상위 PRD를 대체하지 않는다.

## 현재 준비 상태

2026-10-29 이슈 #6 초기 설정을 진행했다. Unity 제외·LFS·TMP 필수 리소스·Noto Sans KR·기본 폰트를 설정하고, 별도 `SetupValidation.unity`에서 한글·동적 fallback·Input System 버튼 반응을 검증했다. [시작하기](../README.md#개발-시작하기-unity)와 [실행 기록](docs/issues/U01-issue-6-initial-setup.md)을 따른다. 앱 기능과 Android APK·실기기 터치는 아직 미구현·미검증이다.
