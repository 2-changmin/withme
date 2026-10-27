# 02. 시스템 구조와 기술 스택

## 구조

앱은 우리 API만 호출하고, 생성과 리깅은 서버의 작업자가 처리한다. 외부 API 키는 서버에만 둔다.

```
┌────────────┐  요청   ┌────────────┐           ┌──────────── 작업자 컨테이너 ────────────┐
│  Unity 앱   │ ─────▶ │ FastAPI 서버 │           │                                       │
│ 화면·관절 보정 │        │ 인증·작업 생성 │           │  ┌────────────┐     ┌──────────────┐ │
│ ·방·재생     │        └─────┬──────┘           │  │Python 작업자 │ ──▶ │ Blender 헤드리스│ │
└─────▲──────┘              │ 작업 등록          │  │ 작업 수령·실행 │     │ 메시 정리·리깅  │ │
      │                     ▼                  │  └──┬──────┬──┘     └──────────────┘ │
      │ 서명 URL로    ┌────────────┐  수령·결과 저장 │     │      │                        │
      └───────────── │  Supabase   │ ◀───────────┼─────┘      │                        │
        GLB 내려받기   │ Auth·DB·큐·  │             └───────────┼────────────────────────┘
                     │ 비공개 버킷    │                         ▼
                     └────────────┘            ┌──────── 외부 API ────────┐
                                               │  Gemini        Meshy     │
                                               │  4면 이미지     3D 생성     │
                                               └─────────────────────────┘
```

처리 순서: 앱 요청 → API가 큐에 작업 등록 → 작업자가 Gemini → Meshy → Blender 순으로 처리하고 결과를 버킷에 저장 → 앱이 서명 URL로 리깅된 GLB를 받는다.

## 기술 스택

### 앱 (Unity) — 황세인

| 영역 | 선택 | 메모 |
|---|---|---|
| 엔진 | Unity 6 LTS (팀 전원 같은 패치 버전) | URP, 모바일 30fps 기준 |
| 언어·UI | C#, uGUI + TextMeshPro | 한글 폰트: Noto Sans KR로 TMP 폰트 에셋 생성 |
| 입력 | Input System | 터치 → 레이캐스트로 쓰다듬는 부위·관절 위치 판정 |
| GLB 로드 | [glTFast](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@latest) | 리깅된 GLB를 런타임에 로드 |
| 애니메이션 | Animator Controller (Generic) | 뼈 이름·계층 경로가 같아서 공용 클립이 모든 캐릭터에 바인딩됨 |
| 외형 수정 | 뼈 스케일 + 색조 보정 셰이더 | `MaterialPropertyBlock`으로 캐릭터마다 값 적용 |
| 소리 | AudioSource | 확률 재생, 재생마다 pitch ±5% |
| 통신 | `UnityWebRequest` + Newtonsoft Json | DTO는 API의 `/openapi.json`(pydantic 스키마)과 같은 이름·필드 |
| 로그인 | Supabase Auth REST (`/auth/v1/otp`, `/auth/v1/verify`) | 토큰은 기기 보안 저장소에 (PlayerPrefs 평문 금지) |
| 파일 선택 | NativeGallery · NativeFilePicker | 갤러리 사진, 녹음·영상 파일 |
| 배포 | Android APK 직접 배포 | iOS는 Apple 개발자 계정이 있으면 TestFlight |

### 3D 처리 (Blender) — 황세인

| 영역 | 선택 | 메모 |
|---|---|---|
| 실행 | Blender 4.x 헤드리스 (`blender -b -P rig.py -- <args>`) | 작업자 컨테이너에 포함 |
| 스크립트 | Blender Python (bpy) | 메시 정리, 관절 추정, 뼈대 맞추기, 자동 웨이트, GLB 내보내기 |
| 사전 제작 | 표준 뼈대 `.blend`, 공용 애니메이션 클립 | 한 번만 만들고 모든 캐릭터가 공유 |

### 생성 (외부 API) — 윤승희 · 이창민

| 영역 | 선택 | 메모 |
|---|---|---|
| 4면 이미지 | Gemini 이미지 편집 API | 원본 여러 장을 참조로 넣어 정체성 유지 (윤승희) |
| 3D 생성 | [Meshy Image-to-3D API](https://docs.meshy.ai/api/image-to-3d.md), Pro 플랜 | 리메시, 목표 2~3만 면, GLB만 요청 (이창민) |

Meshy 리깅 API는 사람형(이족보행) 전용이라 강아지에는 쓰지 않는다. 리깅은 Blender로 직접 한다.

### 서버 — 장세찬 · 박재현

| 영역 | 선택 | 메모 |
|---|---|---|
| 계정·DB·파일 | Supabase (Auth · PostgreSQL · Storage, 서울 리전) | 모든 테이블 RLS |
| API | Python 3.12 · FastAPI | 인증·소유권 검증, 공통 오류 규약, `/docs`에 API 문서 자동 생성 |
| 공유 스키마 | pydantic (`backend/app/schemas.py`) | API와 작업자가 같이 쓰고, Unity C# DTO의 기준 |
| 작업자 | Python + PostgreSQL 작업 큐 (`claim_job`, SKIP LOCKED) | Gemini·Meshy 호출, Blender 실행, FFmpeg 소리 분할 |
| 테스트·CI | pytest, ruff, GitHub Actions | 단위 테스트, 린트, RLS 검사 |

## 저장소 구조

```
withme/
├── unity/                       # 황세인 — 화면·씬·통신·방·캐릭터
├── backend/                     # FastAPI API + 작업자 (Python 프로젝트 하나)
│   ├── app/api/                 # 장세찬·박재현 — API 라우트
│   ├── app/worker/              # 박재현 — 작업 큐 루프, Blender 실행 헬퍼
│   │   └── handlers/            # 작업 종류별 처리 함수 (파일마다 담당자 표시)
│   ├── app/schemas.py           # 관절·캐릭터·작업 형식 (장세찬 관리)
│   └── app/errors.py            # 공통 오류 코드
├── blender/                     # 황세인 — skeleton.blend, animations.blend, rig.py
├── supabase/migrations/         # 테이블·RLS
├── docker-compose.yml           # 로컬 PostgreSQL
└── docs/                        # 이 PRD, 운영 절차, 에셋 출처
```
