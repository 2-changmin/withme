# Unity 로그인 연동 (F01)

담당: **장세찬**(서버·Supabase 설정) → **황세인**(Unity 통신 모듈·로그인 화면)

앱은 로그인을 **Supabase Auth REST에 직접** 요청하고, 받은 access token을 우리 API에 붙여 보낸다.
우리 서버에는 로그인 API가 없다. 서버는 토큰 서명만 검증한다 (`backend/app/auth.py`).

```
앱 ──① 코드 요청 (이메일)──────▶ Supabase Auth ──▶ 메일로 6자리 코드
앱 ──② 코드 검증 (이메일+코드)──▶ Supabase Auth ──▶ access_token · refresh_token
앱 ──③ Authorization: Bearer <access_token> ──▶ 곁에 API
앱 ──④ 토큰 갱신 (만료 전/401 시)──▶ Supabase Auth ──▶ 새 access_token · 새 refresh_token
```

## 공통

| 항목 | 값 |
|---|---|
| Auth 주소 | `https://avxkgpnjevjvdgcnnrbm.supabase.co/auth/v1` |
| 필수 헤더 | `apikey: <publishable 키>` · `Content-Type: application/json` |
| publishable 키 | 팀 비밀 채널에서 받는다 (`sb_publishable_...`). 앱에 넣어도 되는 공개 키다 |

- **secret 키(`sb_secret_...`, service_role)는 절대 앱에 넣지 않는다.**
- `apikey` 헤더가 없으면 모든 요청이 `401 {"message":"No API key found in request"}`로 실패한다.

## ① 코드 요청

```http
POST /auth/v1/otp
apikey: <publishable 키>
Content-Type: application/json

{ "email": "user@example.com", "create_user": false }
```

- 성공: `200 {}`. 메일로 **6자리 코드**가 간다 (보낸 사람 `sw.withme123@gmail.com`, 제목 "곁에 로그인 코드").
- `create_user`는 **항상 `false`**. 초대 전용이라 등록되지 않은 이메일은 거부된다.
- 코드는 **10분** 동안 유효하고 **한 번만** 쓸 수 있다. 다시 요청하면 이전 코드는 무효가 된다.

## ② 코드 검증 → 토큰 발급

```http
POST /auth/v1/verify
apikey: <publishable 키>
Content-Type: application/json

{ "type": "email", "email": "user@example.com", "token": "123456" }
```

성공 `200`:

```json
{
  "access_token": "eyJ...",
  "token_type": "bearer",
  "expires_in": 3600,
  "expires_at": 1761700000,
  "refresh_token": "abc123...",
  "user": { "id": "5782e5ee-...", "email": "user@example.com", ... }
}
```

- `access_token`: 1시간 유효. 우리 API 호출에 쓴다.
- `refresh_token`: access token 갱신용. 오래 유지되므로 access token보다 더 조심해서 저장한다.
- `user.id`: 사용자 UUID. 우리 API의 `user_id`와 같다.

## ③ 우리 API 호출

```http
GET /jobs/{jobId}
Authorization: Bearer <access_token>
```

토큰 문제면 우리 API는 `401`과 공통 오류 형식으로 답한다.

```json
{ "error": { "code": "UNAUTHORIZED", "message": "..." } }
```

`message`는 개발자 확인용이다. 화면 문구는 `code`로 고른다.

## ④ 토큰 갱신

```http
POST /auth/v1/token?grant_type=refresh_token
apikey: <publishable 키>
Content-Type: application/json

{ "refresh_token": "<저장해 둔 refresh_token>" }
```

- 응답 형식은 ②와 같다. **응답의 새 `refresh_token`으로 반드시 교체 저장한다.** 한 번 쓴 refresh token은 곧 무효가 된다.
- 갱신 시점: `expires_at` 1~5분 전, 또는 우리 API가 `401 UNAUTHORIZED`를 줬을 때 1회 갱신 후 원래 요청을 재시도한다.
- 갱신이 실패하면(아래 표) 저장된 토큰을 지우고 로그인 화면(①)으로 보낸다.
- 앱 시작 시 저장된 refresh token이 있으면 바로 갱신해서 로그인 상태를 복원한다 (T11 재로그인).

## 로그아웃

```http
POST /auth/v1/logout
apikey: <publishable 키>
Authorization: Bearer <access_token>
```

성공 `204`. 응답과 관계없이 기기에 저장된 토큰을 모두 지운다.

## 오류 코드 → 화면 처리

Supabase Auth 오류 응답 형식: `{ "code": <HTTP 상태>, "error_code": "...", "msg": "..." }`
**`error_code`로 분기한다.** `msg`는 영어이고 바뀔 수 있으니 화면에 그대로 띄우지 않는다.

| 단계 | HTTP | `error_code` | 상황 | 화면 처리 (예시 문구) |
|---|---|---|---|---|
| ① | 422 | `otp_disabled` | 초대되지 않은 이메일, 또는 이메일 형식 오류 | "초대받은 이메일로만 로그인할 수 있어요. 주소를 다시 확인해 주세요." |
| ① | 429 | `over_email_send_rate_limit` | 같은 이메일로 너무 빨리 다시 요청 (`msg`에 남은 초가 있음) | "잠시 후 다시 요청해 주세요." 재요청 버튼은 **60초** 동안 비활성화 |
| ② | 403 | `otp_expired` | 코드가 틀림 · 10분 지남 · 이미 사용함 | "코드가 맞지 않거나 시간이 지났어요. 새 코드를 받아 주세요." |
| ④ | 400 | `validation_failed` | refresh token 무효 (만료·이미 사용·로그아웃) | 토큰 삭제 → 로그인 화면 |
| 공통 | 401 | (없음) | `apikey` 헤더 누락 | 개발 오류. 출시 전에 없어야 한다 |
| 공통 | — | 네트워크 오류·5xx | 연결 실패 | "잠시 연결이 불안정해요. 다시 시도해 주세요." |

- 초대되지 않은 이메일과 형식 오류는 같은 `otp_disabled`로 온다. 이메일 형식은 앱에서 먼저 검사한다.
- 시간당 발송 한도(프로젝트 전체 30통)를 넘으면 ①이 429로 실패할 수 있다. 같은 문구로 처리한다.

## 토큰 저장

- `access_token`, `refresh_token`은 **기기 보안 저장소**에 둔다 (Android Keystore로 암호화). `PlayerPrefs` 평문 저장 금지 (02-architecture.md).
- 로그에 토큰·코드를 출력하지 않는다.

## 로컬 개발

- 서버를 로컬(`ENV=local`, `AUTH_DEV_BYPASS=true`)로 띄우면 토큰 없이 `X-Dev-User: <아무 UUID>` 헤더로 우리 API를 호출할 수 있다. 로그인 화면이 없어도 다른 화면을 먼저 만들 수 있다.
- 실제 로그인 테스트 계정은 장세찬에게 요청한다 (Supabase Authentication → Users에 등록해야 코드가 발송된다).

## 운영 설정 (장세찬 관리)

| 설정 | 값 |
|---|---|
| 리전 | Seoul (ap-northeast-2) |
| 새 사용자 가입 | 꺼짐 (초대 전용) |
| 이메일 OTP | 6자리, 10분 |
| 메일 발송 | Gmail SMTP `sw.withme123@gmail.com`, 템플릿 "Magic link or OTP"에 `{{ .Token }}` |
| JWT 서명 | ES256 (JWKS). 서버는 `SUPABASE_URL`만 있으면 된다 |
