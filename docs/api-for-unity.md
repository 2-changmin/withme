# Unity API 연동 안내

담당: **박재현**(백엔드) → **황세인**(Unity)

| 항목 | 주소 |
|---|---|
| API | `https://gomagochi-jeahyun.apps.dauai.org` |
| Swagger (직접 호출해 보기) | `https://gomagochi-jeahyun.apps.dauai.org/docs` |
| OpenAPI JSON (C# DTO 생성용) | `https://gomagochi-jeahyun.apps.dauai.org/openapi.json` |

로그인과 토큰 갱신은 [auth-for-unity.md](auth-for-unity.md)를 따른다. 이 문서는 로그인 다음부터다.

## 공통 규칙

- **인증**: 모든 API(`/health` 제외)에 `Authorization: Bearer <access_token>`을 붙인다. Swagger에서는 오른쪽 위 **Authorize**에 토큰을 넣는다.
- **JSON 이름**은 camelCase다 (`petId`, `jobId`, `sourceMediaIds`).
- **오류**는 모두 같은 형식이다. 화면 문구는 `code`로 고르고, `message`는 로그에만 남긴다.

  ```json
  { "error": { "code": "NOT_FOUND", "message": "..." } }
  ```

  | code | HTTP | 앱에서 할 일 |
  |---|---|---|
  | `UNAUTHORIZED` | 401 | 토큰을 갱신하고 한 번 다시 보낸다. 또 실패하면 로그인 화면 |
  | `NOT_FOUND` | 404 | 없는 대상. 남의 데이터도 404로 답한다 |
  | `VALIDATION_FAILED` | 422 | 입력 오류 (형식·크기·개수) |
  | `CONFLICT` | 409 | 순서가 아직 아님 (예: 4면 완성 전에 확정) 또는 이미 있음 |
  | `LIMIT_EXCEEDED` | 429 | 재생성 횟수·업로드 개수 초과. 3D 생성 작업이 이 코드로 실패하면 Meshy 크레딧 부족(#51)이므로 다시 시도 버튼 대신 사진 모드로 안내 |
  | `UPSTREAM_FAILED` | 502 | Gemini·Meshy 장애. 잠시 뒤 다시 |
  | `INTERNAL` | 500 | 서버 오류. 다시 시도 버튼 |

- **오래 걸리는 일**(파일 검사·4면·3D·리깅)은 `202 {"jobId"}`를 바로 돌려준다. 앱은 `GET /jobs/{jobId}`를 **3초마다** 조회한다.
  - `status`: `queued` → `running` → `succeeded` 또는 `failed`
  - `progress`: 0~100 (진행 막대)
  - `result`: 성공하면 결과. 파일은 **서명 URL**로 준다 (`views`, `glbUrl`, `riggedGlbUrl`)
  - 실패하면 `errorCode`·`errorMessage`
- **서명 URL은 금방 만료된다.** 저장하지 말고, 화면을 다시 열 때 `GET /jobs/{jobId}`나 `GET /media/{id}`를 다시 부른다.
- **같은 버튼을 두 번 눌러도 괜찮다.** 생성 API는 같은 요청이면 같은 `jobId`를 돌려준다. `Idempotency-Key` 헤더(아무 UUID)를 붙이면 더 확실하다.

## 전체 순서

```
① POST /pets                                   반려견 등록 (계정당 1마리) → pet.id
   GET  /pets                                  재로그인 후 pet.id 다시 찾기

② POST /pets/{petId}/uploads                   사진마다 업로드 URL 받기 → mediaId, uploadUrl, headers
   PUT  {uploadUrl}  (headers 그대로, 본문=파일)   파일 올리기 (우리 서버가 아니라 저장소로 간다)
   POST /media/{mediaId}/finalize              올렸다고 알리기 → jobId (기다리지 않아도 된다)

③ POST /pets/{petId}/reference-views           {"sourceMediaIds": [1~4개]} → jobId
   GET  /jobs/{jobId}                          succeeded → result.referenceViewId, result.views[4]
   POST /reference-views/{id}/retry            마음에 안 들면 재생성 (최대 3번) → 새 jobId

④ POST /reference-views/{id}/approve           4면 확정 → 3D 생성 jobId
   GET  /jobs/{jobId}                          succeeded → result.characterModelId, result.glbUrl

⑤ GET  /character-models/{id}/joints           자동 추정 관절 (준비 전에는 409 → 3초 뒤 다시)
   POST /character-models/{id}/rig             보정한 관절로 리깅 → jobId
   GET  /jobs/{jobId}                          succeeded → result.riggedGlbUrl  ← glTFast로 로드

⑥ 나의 방 물건 (장난감·간식·애착자리)
   POST /pets/{petId}/items                    {"kind": "toy", "sourceMediaId"} → itemId, jobId (누끼)
   GET  /pets/{petId}/items?kind=toy           목록. status: cutting → cutout → generating → ready
   POST /pets/{petId}/toys/gacha               (포인트 차감 확인 화면 뒤) → itemId, jobId
   GET  /pets/{petId}/items                    뽑힌 장난감이 ready가 되면 glbUrl ← glTFast로 로드
```

## API별 요약

### 반려견

| API | 요청 | 응답 |
|---|---|---|
| `POST /pets` | `{"name": "콩이"}` (1~30자) | `201 Pet`. 이미 있으면 409 |
| `GET /pets` | | `[Pet]` (0~1개) |
| `GET /pets/{petId}` | | `Pet` |
| `PATCH /pets/{petId}` | `{"name": "..."}` | `Pet` |

`Pet`: `{"id", "name", "photoMediaId", "createdAt", "updatedAt"}`

### 업로드

| API | 요청 | 응답 |
|---|---|---|
| `POST /pets/{petId}/uploads` | `{"mimeType": "image/jpeg", "sizeBytes": 123456}` | `201 {"mediaId", "uploadUrl", "method": "PUT", "headers"}` |
| `PUT {uploadUrl}` | 본문 = 파일 바이트, `headers`를 그대로 붙인다. **Authorization은 붙이지 않는다** | `200` |
| `POST /media/{mediaId}/finalize` | | `202 {"jobId"}` |
| `GET /media/{mediaId}` | | `Media` |

- 형식: 사진 `image/jpeg`·`png`·`webp`·`heic`, 영상 `video/mp4`·`quicktime`
- 상한: 사진 장당 10MB·10장, 영상 50MB·3개, 반려견당 전체 200MB. 넘으면 422 또는 429
- `uploadUrl`은 2시간 유효
- `Media`: `{"id", "petId", "sourceType", "mimeType", "sizeBytes", "status", "errorCode", "url", "createdAt"}`
  - `status`: `pending`(URL만 받음) → `uploaded`(올림) → `ready`(검사 끝) / `rejected`
  - **`uploaded`부터 4면 생성에 쓸 수 있다.** finalize 작업을 기다릴 필요 없다

### 캐릭터 생성

| API | 요청 | 응답 |
|---|---|---|
| `POST /pets/{petId}/reference-views` | `{"sourceMediaIds": ["..."]}` 1~4개 | `202 {"jobId"}` |
| `POST /reference-views/{id}/retry` | | `202 {"jobId"}`. 3번 넘으면 429 |
| `POST /reference-views/{id}/approve` | | `202 {"jobId"}`. 4면 완성 전이면 409 |
| `GET /character-models/{id}/joints` | | `Joints`. 준비 전이면 409 |
| `POST /character-models/{id}/rig` | `Joints` (관절 10개 모두) | `202 {"jobId"}` |

작업 결과 (`GET /jobs/{jobId}`의 `result`)

| 작업 `type` | result |
|---|---|
| `reference_views` | `referenceViewId`, `views` [옆, 앞, 뒤, 기타 순서의 URL 4개], `problems` (검사에 걸린 면) |
| `meshy_generate` | `characterModelId`, `glbUrl` |
| `rig` | `characterModelId`, `riggedGlbUrl`, `skeletonVersion` |
| `item_cutout` | `itemId`, `cutoutUrl` |
| `item_views` | `itemId`, `views` [4] |

### 나의 방 물건 (#76)

| API | 요청 | 응답 |
|---|---|---|
| `POST /pets/{petId}/items` | `{"kind": "toy"\|"snack"\|"bed", "sourceMediaId": "..."}` (업로드한 사진 1장) | `202 {"itemId", "jobId"}` |
| `GET /pets/{petId}/items` | `?kind=toy` (생략하면 전부) | `[Item]` (올린 순서) |
| `POST /pets/{petId}/toys/gacha` | | `202 {"itemId", "jobId"}`. 후보 4개 미만이면 409 |

- `Item`: `{"id", "kind", "status", "cutoutUrl", "glbUrl", "jobId", "createdAt"}`
  - `status`: `cutting`(누끼 중) → `cutout`(누끼 완료) → `generating`(4면·3D 중) → `ready`(`glbUrl` 있음)
  - `cutoutUrl`은 투명 배경 PNG. 장난감 목록·가챠 화면에 2D로 보여 준다
- **장난감**은 누끼까지만 만든다. 가챠를 돌려야 그중 1개가 3D가 된다
  - 가챠 버튼: `kind=toy`, `status=cutout`인 장난감이 **4개 이상**일 때만 켠다
  - 이미 3D가 된 장난감은 다시 뽑히지 않는다
  - 포인트는 화면 연출만 한다. 서버에는 포인트가 없다
  - 가챠 요청에 `Idempotency-Key`를 붙이면, 두 번 눌러도 한 번만 뽑힌다
- **간식·애착자리**는 누끼가 끝나면 자동으로 3D까지 만든다
- 진행 상황은 `GET /jobs/{jobId}`로도 볼 수 있지만, 단계마다 jobId가 바뀌므로 목록의 `status`를 보는 편이 쉽다

`Joints` 형식은 [04-api-data.md](04-api-data.md) "관절 데이터 형식"을 따른다. 좌표는 정면 +Z, 바닥 y=0, 키 1로 정규화한 값이고, 왼쪽 관절만 보낸다.

### 무지개다리 편지 (#74)

| API | 요청 | 응답 |
|---|---|---|
| `POST /pets/{petId}/letters` | `{"body": "..."}` (1~2000자) | `201 Letter`. 하루 1통, 넘으면 429 |
| `GET /pets/{petId}/letters` | | `[Letter]` (최근 순) |
| `GET /letters/{id}` | | `Letter`. 답장을 기다릴 때 3초마다 |
| `DELETE /letters/{id}` | | `204` |

`Letter`: `{"id", "petId", "body", "reply", "status", "jobId", "help", "notice", "createdAt", "repliedAt"}`

| status | 화면 |
|---|---|
| `pending` | "답장을 쓰고 있어요" (`GET /letters/{id}`를 3초마다) |
| `replied` | `reply`를 보여 주고, 아래에 **`notice`("AI가 ○○의 모습을 떠올려 쓴 답장이에요")를 항상** 함께 |
| `crisis` | 답장 없음. **`help`(상담 전화 안내)를 그대로** 보여 준다 |
| `failed` | "답장을 쓰지 못했어요". 오늘 횟수에 세지 않으니 다시 쓸 수 있다 |

편지 화면에 "편지 글은 답장을 쓰기 위해 Google(Gemini)에 전달돼요" 안내를 정적 문구로 넣는다.

## 지금 서버 상태 (목업)

서버는 외부 API 비용을 아끼려고 **목업 모드**로 돈다. 응답 형식은 실제와 같다.

- `MOCK_LETTER=true`: 편지 답장은 Gemini 없이 정해진 문장으로 온다. 흐름(pending → replied)은 실제와 같다.
- `MOCK_CHARACTER=true`: 캐릭터 생성 API가 바로 `jobId`(`mock-...`)를 주고, `GET /jobs/mock-...`는 곧바로 `succeeded`다. 4면은 자리표시 이미지, GLB는 예시 주소다.
- 반려견·업로드 API는 실제로 동작한다 (Supabase 저장소).
- 실제 생성(Gemini·Meshy·Blender)이 연결되면 같은 API 그대로 실제 결과가 나온다. 앱 코드는 바꾸지 않아도 된다.

## 없는 것

- 동의 API는 없다. 외부 전송 안내는 앱 화면에 정적으로 넣는다.
- 앨범과 소리 태그는 데모 범위에서 뺐다. 버전·확정·습관(#24) API는 아직이다. 나오면 Swagger에 바로 보인다.
