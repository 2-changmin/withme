# U03·U04 / #8 — 통신 모듈·로그인 화면 작업 계획

- 작성일: 2026-10-30
- 이슈: [#8 — 통신 모듈 + 로그인 화면](https://github.com/2-changmin/withme/issues/8), OPEN. 확인한 최신 마감: 2026-10-31.
- 상태: 통신·세션·01·02 화면 구현 및 PC 자동 검증 완료. 실제 이메일 OTP 로그인·사용자 화면 확인·Android 저장소 동작은 미검증.
- 범위: 공통 HTTP 통신, 이메일 코드 로그인, 세션·토큰 관리, 와이어프레임 01·02, 로그인 후 진입 상태 조회.
- 기존 `blender/work-plan.md`와 진행 중인 관절·뼈대 작업은 보존한다. #7 샘플 검증 씬을 제품 로그인 씬으로 바꾸지 않는다.

## 1. 확인한 API와 화면 기준

2026-10-30 배포 Swagger와 OpenAPI를 직접 읽었다. 웹 열기 도구로 접근되지 않아 HTTPS GET으로 OpenAPI를 확인했으며, 인증 코드 발송·사용자 자료 변경·생성 요청은 하지 않았다.

| 대상 | 확인 내용 | 이번 작업에서 사용할 범위 |
|---|---|---|
| 앱 API | `https://gomagochi-jeahyun.apps.dauai.org`, OpenAPI 0.1.0 | 서버 환경 설정·Bearer 인증·공통 오류 |
| `GET /health` | 실제 200, `status: ok`, `env: prod` | 연결 확인. 로그인 성공 근거로 사용하지 않음 |
| `GET /pets` | 인증 필요, 성공 시 `Pet[]`, 설명상 0~1마리 | 로그인 후 `pet_id` 복원·등록 여부 확인 |
| 미인증 `GET /pets` | 실제 401, `error.code: UNAUTHORIZED` | 서버 오류 형식·미인증 처리 확인 |
| Supabase Auth | 앱 Swagger에 로그인·갱신 API 없음 | 별도 [인증 계약](../../../docs/auth-for-unity.md)에 따라 직접 호출 |
| 01 로그인 | 이메일 주소·인증 코드 받기 | [01-login.svg](../wireframe/01-login.svg) 그대로 구현 |
| 02 코드 확인 | 6자리 코드·재발송·인증·주소 변경 | [02-verification.svg](../wireframe/02-verification.svg) 기준 |

Supabase Auth 주소는 기존 계약의 `https://avxkgpnjevjvdgcnnrbm.supabase.co/auth/v1`을 사용한다. publishable 키·실제 테스트 계정·현재 운영 OTP 설정은 구현 전 확인할 입력이다. `/health`와 미인증 응답 확인으로 Supabase 로그인까지 검증했다고 표시하지 않는다.

## 2. Swagger와 와이어프레임 사이의 미완성 연결

- Swagger의 `Pet`에는 `id`, `name`, `photoMediaId`, `createdAt`, `updatedAt`이 있다. 최종 모델 확정 여부·확정 GLB URL·습관 완료 상태는 없다.
- `GET /pets`에서 한 마리가 반환됐다는 사실만으로 등록·모습 확정 완료라고 판단하지 않는다. `photoMediaId` 존재 여부도 최종 확정 근거가 아니다.
- 현재 Swagger에는 로그인 후 전체 제작 상태를 복원하는 집계 API나 확정 모델 조회 계약이 없다. 작업 조회 API는 있지만 기존 `job_id`를 모르는 새 로그인에서 작업을 찾는 계약은 별도 확인이 필요하다.
- #8에서는 인증 성공·반려견 유무·진입 판단에 필요한 상태를 전달하는 연결점까지 만든다. 미제공 상태를 임의 응답 필드나 로컬 플래그로 확정하지 않는다.
- 미등록 사용자는 03 동의·등록 흐름으로 연결하되 해당 화면의 실제 구현은 #9 범위다. 기존 반려견의 확정 상태를 판정할 수 없으면 안전한 상태 확인·재시도 화면을 제공한다. 무조건 새 등록을 시키거나 3D 방으로 보내지 않는다.
- 최종 모델 확정 상태를 검증할 계약이 준비되면 앱 시작·로그인 복원 후 매번 3D 메인으로 진입하는 기존 와이어프레임 규칙을 연결한다. 세부 API 확장은 서버 담당자와 별도 합의한다.

## 3. 구현 구조

기존 Unity 6000.0.76f1, uGUI·TMP·Input System, Newtonsoft Json을 재사용한다. `Assets/Withme/` 아래에 제품 코드와 UI를 두는 것을 기본안으로 하고 실제 파일 배치는 구현 시 기존 구조와 대조한다.

| 구성 | 책임 |
|---|---|
| 환경 설정 | 앱 API 주소·Auth 주소·publishable 키, 목/실서버 구분. secret/service_role 키 제외 |
| HTTP 클라이언트 | UnityWebRequest 생성·JSON·상태 코드·시간 제한·취소·자원 해제 |
| Auth 클라이언트 | OTP 요청·코드 검증·refresh·logout. 앱 API와 다른 응답 형식 처리 |
| 세션 관리자 | 로그인 상태·만료·갱신 요청 합치기·로그아웃·계정 전환 처리 |
| 토큰 저장소 인터페이스 | 저장·읽기·교체·삭제, 구현별 실패 처리 |
| 오류 문구 변환 | 앱 `error.code`, Auth `error_code`를 한국어 안내로 변환 |
| 로그인 화면 제어 | 01·02 화면 입력·대기·중복 탭·취소·재발송·오류 표시 |
| 시작 상태 판정 | 세션 복원→인증된 `GET /pets`→후속 흐름에 상태 전달 |

앱 Bearer 토큰은 지정된 앱 API 요청에만 첨부한다. Supabase에는 해당 요청에 필요한 헤더만 사용한다. 파일 다운로드·서명 URL·임의 호스트로 토큰을 자동 전달하지 않는다.

## 4. 인증·세션 흐름

1. 앱 시작 시 저장소에서 세션을 읽는다. 세션이 없으면 01로 이동한다.
2. 복원할 refresh token이 있으면 갱신한다. 네트워크 실패와 무효 토큰을 구분한다.
3. 이메일 입력을 검증한 뒤 `POST /otp`에 `{email, create_user:false}`를 보낸다. 성공 후에만 02로 이동한다.
4. 02에서 숫자 6자리를 입력하고 `POST /verify`에 `{type:"email", email, token}`을 보낸다.
5. 받은 access token·refresh token·만료 정보를 일관되게 저장한 뒤 세션을 인증 완료로 전환한다. 저장이 실패했는데 영구 로그인 성공으로 표시하지 않는다.
6. 인증된 `GET /pets`로 반려견 유무를 확인하고 후속 화면에 상태를 전달한다. 이 조회가 실패하면 재로그인으로 무조건 되돌리지 않고 재시도를 제공한다.
7. 만료 전 또는 앱 API의 401에서 세션 갱신을 한 번 수행한다. 동시에 여러 요청이 실패해도 refresh 요청은 하나로 합친다.
8. 갱신 성공 시 새 refresh token까지 교체한다. 재요청은 읽기 등 안전한 요청부터 한 번만 허용한다. 부작용이 있는 POST를 무조건 반복하지 않는다.
9. 무효·폐기된 refresh token이면 저장소를 지우고 01로 이동한다. 연결 실패·시간 초과·5xx만으로 저장된 토큰을 즉시 삭제하지 않는다.
10. 로그아웃은 서버 응답과 관계없이 로컬 토큰·사용자 상태를 정리한다. 이전 세션의 늦은 갱신 응답이 로그아웃 후 토큰을 다시 저장하지 못하게 한다.

Auth 요청 자체의 401에는 앱 API용 refresh 재시도를 적용하지 않는다. publishable 키 누락·설정 오류를 무한 갱신으로 처리하지 않는다. 메일 발송 요청은 서버가 처리했는지 불명확할 때 자동 반복하지 않는다.

## 5. 와이어프레임 구현과 화면 상태

- 01: 기존 제목·설명·AI 안내·‘이메일 주소’·‘인증 코드 받기’ 유지. 초대 정책은 서버 요청의 `create_user:false`와 실제 오류 처리로 지킨다.
- 02: 6자리 숫자 입력, 붙여넣기, 키보드 제출, ‘인증하고 시작하기’, ‘코드 다시 받기’, ‘이메일 주소 바꾸기’를 구현한다.
- 코드 입력은 한 개의 TMP 입력 컴포넌트로 관리하는 것을 우선 검토한다. 여섯 자리 시각 표현 때문에 입력 포커스·붙여넣기가 깨지지 않게 한다.
- 대기 중 중복 탭을 막고 로딩·실패 문구를 명확히 표시한다. 입력 전부터 오류 문구를 항상 띄우지 않는다.
- 인증 계약의 재발송 간격 60초·코드 유효 시간 10분을 기준으로 하되 실제 Supabase 운영 설정과 대조한다. 코드 유효성의 최종 판단은 서버가 한다. 화면 이탈로 재발송 제한이 초기화되지 않게 한다.
- 주소 변경 시 코드·이전 오류를 정리한다. 늦게 도착한 이전 이메일 요청 결과가 현재 화면을 바꾸지 못하게 한다.
- 공통 Canvas·TMP 폰트·안전 영역·스크롤·키보드 가림·뒤로가기 기준은 [모바일 명세](../mobile-platform.md)를 따른다. 제품 시작 씬을 추가하되 기존 SampleScene·검증 씬은 보존한다.

## 6. 오류 처리

| 오류 | 처리 계획 |
|---|---|
| 이메일 형식·코드 자리수 오류 | 요청 전 입력칸 주변에 안내 |
| Auth `otp_disabled` | 초대 정책에 맞는 이메일 확인 안내. 영어 원문 직접 노출 금지 |
| Auth `otp_expired` | 틀림·만료·사용 완료를 포괄하는 안내와 새 코드 받기 |
| Auth 429 | 발송 제한 안내·재요청 대기. 자동 메일 재발송 금지 |
| 앱 API `UNAUTHORIZED` | 1회 갱신, 실패 종류에 따라 재인증 또는 연결 재시도 |
| `FORBIDDEN`, `NOT_FOUND`, `CONFLICT`, `VALIDATION_FAILED` 등 | 코드별 한국어 문구. 인증 실패로 일괄 처리하지 않음 |
| 네트워크·시간 초과·5xx | 입력·유효 세션 보존, 중복 처리 없는 재시도 경로 |
| 비JSON·빈 본문·알 수 없는 코드 | HTTP 상태와 일반 안내로 안전하게 처리. 본문 파싱 예외로 화면 중단 금지 |

토큰·인증 코드·요청 본문·인증 헤더는 로그에 출력하지 않는다. 화면에 서버 `message`나 Auth `msg`를 그대로 노출하지 않는다.

## 7. 저장소와 PC 우선 검증 범위

- `ITokenStore`로 분리하고 Android는 Keystore로 보호되는 암호화 저장을 구현한다. 구현 전 지원 중인 라이브러리 또는 작은 네이티브 브리지를 검토해 선택한다. PlayerPrefs·평문 JSON 저장은 사용하지 않는다.
- PC 에디터 기본 검증은 메모리 저장소를 사용한다. 세션 재생성·저장 실패·토큰 교체·복구는 주입 가능한 저장소 대역으로 검사한다. 이를 실제 기기 영구 저장 검증으로 보고하지 않는다.
- PC에서 실제 영구 로그인 유지가 필요하면 Windows 보안 저장소 어댑터를 별도 구현한다. Android 코드를 에디터에서 실행할 수 있다고 가정하지 않는다.
- 목 응답은 편집기·개발 구성에만 명시적으로 선택하며 실제 배포 서버에 `X-Dev-User` 인증 우회를 보내지 않는다.
- APK 생성·매번 Android 빌드·실기기 검증은 이번 PC 반복 작업에서 수행하지 않는다. Android 저장·복원·키보드·앱 복귀는 후속 기기 확인 대상으로 남긴다.

## 8. 작업 순서와 검증 조건

| 단계 | 구현·확인 | 완료 근거 |
|---|---|---|
| 1. 계약·환경 정리 | OpenAPI 기준 DTO·오류 형식, Auth 별도 설정, 누락 진입 상태 구분 | 읽기 전용 연결 확인·계약 테스트 |
| 2. 통신 모듈 | JSON·Bearer 첨부 범위·취소·시간 초과·오류 파싱 | 정상·401·429·5xx·비JSON·빈 응답·취소 검사 |
| 3. 세션·저장소 | OTP·refresh·복원·logout·인터페이스, Android 저장 구현 경로 선정 | 동시 401의 갱신 1회, 토큰 회전·갱신 실패·로그아웃 경쟁 조건 검사 |
| 4. 01·02 화면 | 와이어프레임을 uGUI·TMP로 구현 | PC Play Mode 입력·붙여넣기·중복 탭·재발송·주소 변경·뒤로가기 확인 |
| 5. 시작 흐름 | 인증 후 `/pets` 조회·미등록/기존 자료 분기 연결점 | 빈 목록·기존 반려견·조회 실패·확정 상태 미제공 분기 확인 |
| 6. 실인증 | 설정·계정 준비 후 사용자 이메일·코드로 로그인 | 실제 Supabase 토큰→인증된 `/pets` 요청 결과. 목 통과와 분리 |
| 7. 사용자 확인·기록 | PC 화면·문구·오류/성공 흐름 확인 | 구현·사용자 확인·모바일 미검증을 각각 기록 |

통신·세션 핵심 로직은 주입 가능한 전송/시계/저장소로 검증하고, 실제 화면·취소·화면 전환은 Play Mode에서 확인한다. 형식적인 UI 속성 테스트를 늘리지 않는다. 인증 정보가 없으면 독립 구현과 목 검증까지 진행하고 실인증은 미실행으로 남긴다.

## 9. 구현 전 필요한 정보와 남는 의존성

| 항목 | 필요 시점 | 없을 때 진행할 부분 |
|---|---|---|
| Supabase publishable 키·테스트 이메일 | 실인증 단계 | 통신·세션·UI·목 검증 |
| 운영 OTP 설정·재발송 정책 확인 | 재발송 UI·실인증 | 현재 인증 문서 값을 설정값으로 사용하고 실제 동작은 미검증 표시 |
| 확정 모델·생성 중 상태의 복원 API | 기존 사용자 최종 화면 연결 | 인증·반려견 조회, 상태 확인/재시도 화면과 연결점 |
| Android 보안 저장소 방식·지원 버전 | 저장 구현 | 인터페이스·PC 메모리 구현·저장소 실패/복구 테스트 |

인증·토큰 저장·외부 전송에 해당하는 변경은 PR 단계에서 기존 규칙에 따라 장세찬·박재현의 리뷰를 받는다. 이번 계획 작성은 리뷰 요청·API 변경·GitHub 댓글 게시를 포함하지 않는다.

## 참고

- [배포 Swagger](https://gomagochi-jeahyun.apps.dauai.org/docs) · [조회한 OpenAPI](https://gomagochi-jeahyun.apps.dauai.org/openapi.json)
- [Unity 인증 계약](../../../docs/auth-for-unity.md)
- [와이어프레임 v0.5](../wireframe/README.md)
- [Unity API 연결 기준](../integration.md)

## 실행 결과 — 2026-10-30

### 구현한 것

- 제품 시작 씬 `Assets/Withme/Scenes/Start.unity`를 만들고 Build Settings 첫 씬으로 추가했다. 기존 SampleScene·#7 검증 씬·Blender 작업은 보존했다.
- `Runtime/Networking/JsonHttpClient.cs`: UnityWebRequest 비동기 전송, JSON, 취소·시간 제한·리소스 정리, 앱/Auth 오류 형식 분리, 다른 호스트·기준 경로 이탈 거부, 리다이렉트 비활성화.
- `Runtime/Auth/`: OTP·검증·갱신·로그아웃, 요청별 Bearer 첨부, 동시 갱신 합치기, refresh token 교체, 네트워크 실패 시 세션 보존, 무효 refresh 삭제, 로그아웃과 늦은 갱신의 경쟁 방지, 저장 실패 처리.
- `Runtime/UI/LoginFlow.cs`: 01·02 시안 기반 이메일·코드 입력, 한 입력 컴포넌트/여섯 칸 표시, 입력 검증·붙여넣기, 재발송 제한·중복 탭 방지·주소 변경 취소, 조회 재시도·로그아웃.
- 인증 후 `GET /pets`가 비어 있으면 `RegistrationRequested` 연결점을 제공한다. #9 등록 화면 자체는 이번에 구현하지 않았다. 기존 반려견이 있어도 확정 상태 API가 없으면 상태 확인 안내에 머물고 3D 방에 들어가지 않는다.
- Android는 `Assets/Plugins/Android/org/withme/auth/SecureSessionStore.java`와 C# JNI 어댑터를 추가했다. AndroidKeyStore AES-GCM 키로 세션을 암호화해 앱 전용 SharedPreferences에 저장하며 평문 세션 파일·PlayerPrefs를 사용하지 않는다. 삭제 시 키를 제거해 남은 암호문을 무효화한다.
- Java PluginImporter는 Android만 활성화했다. JSON DTO 보존을 위한 `Assets/Withme/link.xml`을 추가했다. APK나 IL2CPP 플레이어 빌드를 실행한 것은 아니다.
- `Assets/Withme/Settings/ConnectionSettings.asset`에 배포 API/Auth 주소와 사용자가 제공한 publishable 키를 반영했다. publishable 키는 앱용 공개 키이며 secret/service_role 키가 아니다. 이메일·코드·실제 세션 토큰은 저장소에 넣지 않았다.
- 저장된 씬의 `useDemoResponses` 기본값은 false다. 편집기/개발 빌드에서만 명시적으로 켤 수 있는 목 응답은 메일을 보내지 않으며 검증 코드가 123456이다. 검증 중에는 표시 문구로 목 모드임을 구분한다.

### 실행한 검증

| 검증 | 결과와 범위 |
|---|---|
| Unity C# 컴파일 | 오류 0·경고 0 |
| `verify-auth.cs` | 8개 검사 묶음 통과: OTP 계약·헤더, 호스트/경로·취소 거부, 비정상/빈/네트워크 응답, 재발송·갱신 합치기·회전, 로그아웃 경쟁, 복원 실패 구분, 저장 실패, 401 재요청 상한 |
| `verify-login-view.cs` | PC Play Mode 7개 흐름 통과. 임시 Input System 마우스로 실제 Button을 클릭해 코드 오류·재발송 유지·여섯 자리 표시·미등록 분기·로그아웃·기존 반려견의 방 진입 차단·조회 실패/재시도·늦은 응답 무시 확인 |
| 입력 자동화 설정 | 임시 InputSettings와 Mouse 장치를 사용한 뒤 원본 복원·장치 제거. 프로젝트에 테스트 입력 설정을 저장하지 않음 |
| `verify-auth-live.cs` | 실제 UnityWebRequest로 `/health` 정상 및 미인증 `/pets` 401/UNAUTHORIZED 확인. 사용자 인증 데이터는 조회하지 않음 |
| publishable 키 | Supabase `/auth/v1/settings` 읽기 전용 요청 200. 이메일 인증 활성화·신규 가입 차단 확인. OTP 길이/만료는 공개 설정 응답으로 확인하지 못했으며 기존 인증 문서 기준 유지 |
| Android Java | 설치된 OpenJDK 17과 Android 36 android.jar로 javac 컴파일 통과 |
| Android C# 경로 | 에디터 조건부 컴파일에서 제외되는 JNI 어댑터를 별도 dry-run 컴파일하여 diagnostics 없음 확인. JNI 호출 실행이나 기기 저장 성공 근거는 아님 |
| 시각 확인 | 390 × 844 Game View 이메일·코드 화면 캡처 확인. 수신 이메일 라벨 자동 크기 상한을 14로 보정 |
| 콘솔 | PC 검증 후 오류·경고 0 |

초기 비정상 오류 본문 검사에서 파싱 예외를 확인해 오류 토큰의 객체/문자열 타입을 확인하도록 보완하고 재컴파일 후 통과했다. 캡처는 로컬 Codex 시각화 폴더의 `login-email.png`, `login-code.png`에 보관하며 프로젝트 임시 캡처는 제거한다.

### PC에서 확인하는 방법

1. Unity 6000.0.76f1로 `Assets/Withme/Scenes/Start.unity`를 열고 Play한다. 실제 연결 기본값은 `useDemoResponses = false`다.
2. Supabase에 초대된 이메일을 입력해 ‘인증 코드 받기’를 누른다. 이 조작은 실제 메일 발송 요청이다. 받은 코드를 입력해 로그인한다.
3. 현재 #8 범위에서는 로그인 후 미등록 안내 또는 기존 자료 상태 확인까지만 연결된다. 실제 등록 화면과 확정 3D 메인 연결은 후속 작업이다.
4. 메일 없이 UI만 보려면 Play를 종료하고 LoginFlow의 `useDemoResponses`를 켠 뒤 Play한다. 실제 이메일을 쓸 필요 없이 유효한 형식의 예제 주소와 코드 123456으로 확인한다. 확인 후 false로 복원한다.
5. PC 저장소는 메모리이므로 Play 종료 시 로그인 유지 정보가 사라진다. 실제 Android 영구 저장과 다르다.

### 남은 확인과 완료 판단

- 실제 초대 이메일·코드로 로그인하고 인증된 `/pets` 응답을 확인하는 테스트는 미실행이다. 자동 검증에서 실제 메일을 발송하지 않았다.
- Android Keystore 저장·재실행 복원·키 무효화·플러그인 포함, 모바일 키보드·뒤로가기·앱 복귀는 기기에서 미검증이다. Java minification을 켜는 배포 구성에서는 JNI 클래스 유지 설정도 함께 확인한다.
- 계정 인증은 구현했지만 확정 모델·작업 복원 API의 부재는 그대로다. 등록 여부만으로 최종 화면을 결정하지 않는다.
- PC 사용자 직접 확인은 대기한다. #8을 종료하거나 GitHub에 완료 댓글을 남기지 않았다. 구현과 기기 보안 저장소 검증 완료를 구분한다.

### 구현 근거

- [Unity Java/Kotlin 소스 플러그인](https://docs.unity3d.com/6000.0/Documentation/Manual/AndroidJavaSourcePlugins.html)
- [Android Keystore](https://developer.android.com/privacy-and-security/keystore)
- [Supabase 이메일 OTP](https://supabase.com/docs/guides/auth/auth-email-passwordless)
