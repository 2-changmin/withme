# 04. 백엔드 API와 데이터

담당: **장세찬**(계정·캐릭터 쪽 API), **박재현**(미디어·작업 큐·작업자 실행 환경·앨범·소리)

생성과 리깅은 수 분씩 걸리므로 모두 **작업 큐의 비동기 작업**으로 처리하고, 앱은 `GET /jobs/:id`를 3초마다 조회한다. 생성 요청은 모두 idempotency key를 받아 중복 청구를 막는다.

## API

### 계정·프로필 — 장세찬

| API | 하는 일 |
|---|---|
| `POST /auth/*` | Supabase 인증 코드 로그인 (앱이 Supabase REST 직접 호출, 서버는 토큰 검증) |
| `POST /pets`, `PATCH /pets/:id` | 반려견 프로필 생성·수정 (계정당 1마리, 두 번째는 409) |
| `GET /pets`, `GET /pets/:id` | 내 반려견 조회 (재로그인 후 pet_id 복원) |
| `POST /pets/:id/consents`, `GET /pets/:id/consents` | 동의 기록 (Gemini·Meshy 전달), 항목별 최신 동의 조회 |

### 캐릭터 생성 — 장세찬

| API | 하는 일 |
|---|---|
| `POST /pets/:id/reference-views` | `{"sourceMediaIds": [1~4장]}` → 4면 이미지 생성 작업 시작 → `jobId`. **Gemini 전달 동의 필요** |
| `POST /reference-views/:id/retry` | 4면 재생성 (최대 3회, 넘으면 429) |
| `POST /reference-views/:id/approve` | 4면 확인 → 3D 생성 작업 시작 → `jobId`. **Meshy 전달 동의 필요** |
| `GET /character-models/:id/joints` | 자동 추정한 관절 좌표 (메시 정리 전에는 409) |
| `POST /character-models/:id/rig` | 보정된 관절 좌표로 리깅 작업 시작 → `jobId` |
| `POST /pets/:id/character-versions` | 외형 수정값 저장 (저장할 때마다 새 버전) |
| `POST /pets/:id/character-approvals` | 명시적 확정. 확정 전에는 방 진입 API가 막힘 |
| `PUT /pets/:id/preferences` | 습관 설정 |

### 캐릭터 생성 흐름 (F04~F07, #23)

`MOCK_CHARACTER=false`일 때 실제 테이블·작업 큐로 돈다 (`backend/app/characters.py`). `true`면 목 응답.

1. `POST /pets/:id/reference-views` → `reference_views` 작업. 결과 `referenceViewId`·`views`(URL 4장)·`problems`
2. 보호자 확인 → `POST /reference-views/:id/approve` → `meshy_generate` 작업. 결과 `characterModelId`·`glbUrl`
3. Meshy가 끝나면 작업자가 `mesh_prepare`를 **자동 등록**한다 (`backend/app/pipeline.py`). 앱은 `GET /character-models/:id/joints`가 200이 될 때까지 기다린다
4. 관절 보정 → `POST /character-models/:id/rig` → `rig` 작업. 결과 `riggedGlbUrl`

- **외부 전송 동의**: 동의가 없으면 403 `FORBIDDEN`. 작업자도 전송 직전에 한 번 더 확인한다 (Gemini·Meshy 데이터 처리 ADR).
- **중복 청구 방지**: 4면 생성은 `Idempotency-Key` 헤더, 재생성은 같은 시도당 1번, 승인은 4면 1세트당 3D 모델 1개. 다시 눌러도 같은 `jobId`를 돌려준다.
- **파일은 media ID로 주고받는다.** `GET /jobs/:id`가 결과의 ID 옆에 서명 URL을 붙여 준다 (`viewMediaIds`→`views`, `rawGlbMediaId`→`glbUrl`, `preparedGlbMediaId`→`preparedGlbUrl`, `riggedGlbMediaId`→`riggedGlbUrl`). URL은 300초 뒤 만료되므로 다시 조회한다.

**작업 payload·result 약속** (처리 함수 담당자는 result에 아래 키를 담는다. 후처리가 이 키로 테이블을 채운다)

| 작업 | payload (API·후처리가 보냄) | result (처리 함수가 돌려줌) |
|---|---|---|
| `reference_views` | `petId`, `referenceViewId`, `sourceMediaIds`, `attempt` | `referenceViewId`, `viewMediaIds`[4], `problems` |
| `meshy_generate` | `characterModelId`, `petId`, `viewMediaIds`[4] | `characterModelId`, `rawGlbMediaId`, (`meshyTaskId`) |
| `mesh_prepare` | `characterModelId`, `petId`, `rawGlbMediaId` | `characterModelId`, `preparedGlbMediaId`, `jointsAuto` |
| `rig` | `characterModelId`, `petId`, `preparedGlbMediaId`, `joints` | `characterModelId`, `riggedGlbMediaId`, `skeletonVersion` |

### 미디어·작업·앨범·소리 — 박재현

| API | 하는 일 |
|---|---|
| `POST /pets/:id/uploads` | 업로드 URL 발급 (형식·용량·잔여 용량 확인) |
| `POST /media/:id/finalize` | 업로드 완료 → 검사 작업 등록 |
| `GET /jobs/:id` | 작업 상태·진행률·결과 URL |
| `GET/POST/PATCH/DELETE /memories` | 추억 앨범 |
| `GET /pets/:id/sounds`, `PATCH /sounds/:id` | 잘린 소리 조각 조회, 보호자 태그 수정·빼기 |

### 업로드 흐름 (F03, #30)

1. `POST /pets/:id/uploads` `{"mimeType", "sizeBytes"}` → `{"mediaId", "uploadUrl", "method": "PUT", "headers"}`
2. 앱이 `uploadUrl`에 `headers`를 붙여 파일 본문을 `PUT`
3. `POST /media/:id/finalize` → 실제 크기 재확인 후 `media_check` 작업 등록 → `{"jobId"}`
4. `GET /media/:id` → `status`(`pending`·`uploaded`·`ready`·`rejected`)와 짧은 만료(기본 300초) 서명 `url`

| 종류 | 형식 | 장당 | 반려견당 |
|---|---|---|---|
| 사진 | jpeg·png·webp·heic·heif | 10MB | 10장 |
| 영상 | mp4·mov | 50MB | 3개 |
| 소리 | m4a·mp3·wav·aac | 20MB | 5개 |

전체 200MB. 장당 상한은 Supabase Free 플랜의 파일당 최대 50MB를 넘을 수 없다. 상한은 `backend/app/media.py`의 `KINDS`·`TOTAL_BYTES`에 있다.
작업자는 `media.load(id, pet_id)`, `media.save_generated(...)`, `media.signed_url(id)`(Meshy에 넘길 URL)만 쓴다.

## 작업 종류 (작업 큐) — 박재현

| 종류 | 처리 코드 담당 | 하는 일 |
|---|---|---|
| `media_check` | 박재현 | 파일 시그니처·디코딩 확인, 썸네일, 회전 보정, 위치 메타데이터 제거 |
| `reference_views` | 윤승희 | Gemini로 4면 이미지 생성 → 버킷 저장 |
| `meshy_generate` | 이창민 | Meshy 3D 생성 → GLB 버킷 복사 → Meshy 작업 삭제 |
| `mesh_prepare` | 황세인 | Blender로 메시 정리 + 관절 자동 추정 |
| `rig` | 황세인 | Blender로 자동 리깅 → 리깅 GLB 저장 |
| `sound_split` | 박재현 | FFmpeg로 오디오 추출, `silencedetect`로 조각 분할 |

박재현은 큐(`enqueue/claim/heartbeat/complete/fail`, lease 만료 복구)와 **Blender가 들어간 작업자 컨테이너**를 만들고, 각 담당자는 자기 작업 종류의 처리 함수만 작성한다.

## 테이블

| 테이블 | 주요 컬럼 | 소유자 |
|---|---|---|
| `users`, `pets`, `consents` | 프로필, 동의 항목·시각 | 장세찬 |
| `reference_views` | `pet_id`, `source_media_ids[]`, `view_media_ids[4]`, `attempt`(1~3), `retry_of`, `job_id`, `approved_at` | 장세찬 |
| `character_models` | `pet_id`, `reference_view_id`(unique), `meshy_task_id`, `raw_glb_media_id`, `prepared_glb_media_id`, `joints_auto`, `joints_final`, `rigged_glb_media_id`, `skeleton_version`, `job_id` | 장세찬 |
| `character_versions` | `pet_id`, `character_json`, `approved_at` | 장세찬 |
| `preferences` | 반기는 방식, 자는 자세, 선호 장난감, 활동성 | 장세찬 |
| `media_assets` | `source_type`, `parent_id`, 경로, 상태 | 박재현 |
| `memories` | 글 원문, 연결 미디어, `linked_object` | 박재현 |
| `sound_clips` | 원본 미디어, 구간, 태그(짖음·낑낑·하울링·기타) | 박재현 |
| `jobs`, `audit_events` | 작업 큐, 감사 로그 | 박재현 |

모든 테이블에 RLS를 걸고 CI에서 검사한다. 이미지와 GLB는 비공개 버킷에 저장하고 짧은 만료 서명 URL로만 내려준다.

## 캐릭터 데이터 형식 (`backend/app/schemas.py`의 `Character`)

```json
{
  "modelRef": "character_models/<id>",
  "skeletonVersion": 1,
  "params": {
    "earScale": 1.0, "earAngle": 0,
    "tailScale": 1.0, "legLength": 1.0, "bodyLength": 1.0, "headScale": 1.0,
    "hueShift": 0, "brightness": 0,
    "accessory": "collar_red"
  }
}
```

## 관절 데이터 형식

```json
{
  "skeletonVersion": 1,
  "space": "normalized",
  "joints": {
    "nose":       [0.00, 0.42, 0.61],
    "neck":       [0.00, 0.40, 0.38],
    "tail_base":  [0.00, 0.37, -0.35],
    "tail_tip":   [0.00, 0.48, -0.58],
    "front_paw_l":[0.09, 0.00, 0.30],
    "shoulder_l": [0.08, 0.30, 0.30],
    "hind_paw_l": [0.09, 0.00, -0.28],
    "hip_l":      [0.08, 0.31, -0.27],
    "ear_tip_l":  [0.07, 0.62, 0.45],
    "ear_base_l": [0.05, 0.52, 0.42]
  }
}
```

좌표는 정리된 메시 기준(정면 +Z, 바닥 y=0, 키 1로 정규화)이다. 오른쪽 관절은 x를 뒤집어 만든다.

## 연결 지점 (먼저 올릴 것)

제공자는 마감일까지 `backend/app/schemas.py`에 pydantic 모델을 먼저 올린다. Unity는 `/docs`(OpenAPI)를 보고 C# DTO를 만든다. 구현 전에는 목(mock) 응답으로 개발한다.

| 제공자 → 사용자 | 제공물 | 마감 |
|---|---|---|
| 황세인 → 장세찬 | 관절 JSON 형식, 표준 뼈대 규격 | 10/28 |
| 박재현 → 전원 | 작업 큐 인터페이스, 작업자 컨테이너, 서버 주소 | 10/29 |
| 장세찬 → 황세인 | 로그인 순서·오류 코드 문서, 캐릭터 생성 API 목 응답 | 10/29 |
| 윤승희·이창민 → 박재현 | 작업 처리 함수 (`reference_views`, `meshy_generate`) | 10/31 |
| 박재현 → 황세인 | 업로드·앨범·소리 API | 11/01 |
