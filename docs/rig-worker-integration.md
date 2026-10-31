# 로컬 리깅 연결과 실제 배포의 경계

2026-10-30, F01~F04. backend API·작업 enum·배포 설정은 수정하지 않았다.

## 확인한 결과

- worker와 같은 Blender 4.2.0으로 Git 10관절 JSON→GLB 재생성 성공.
- `blender/worker_adapter.py`: 기본 제한 120초, 입력 해시로 동일 작업 재사용, 잘못된 입력·누락 산출물·타임아웃·종료 코드와 report 불일치 거부. 단위 검사 8개 통과.
- 종료 0은 report `ok`, 종료 2는 `needs_review`일 때만 산출물로 반환한다. 종료 2를 품질 승인으로 바꾸지 않는다. 예외 종료·실패 report는 실패다.
- 원래 메시 3개를 2만 삼각형 이하로 준비하고 17클립 재리깅. 품질 경고 m1=13/m2=5/m3=18, 모두 `accepted:false`다.
- 로컬 HTTP 목에서 관절→리깅→조회→5,829,292바이트 GLB 다운로드 확인. 실제 로컬 Blender 실행이며 외부 생성 호출은 0회다.

## 담당자 확인이 필요한 계약

| 항목 | 현재 간극 | 확인 대상 |
|---|---|---|
| 준비 메시 | 생성 결과는 `glbUrl`, 관절은 normalized다. 보정 화면에 같은 공간의 준비 메시를 얻을 방법이 확정되지 않았다. | 장세찬·황세인, #17/#11 |
| 품질 | 실제 작업 상태는 queued/running/succeeded/failed다. needs_review 및 사람의 승인 여부를 어디에 표현할지 미확정이다. | 박재현·장세찬·황세인 |
| 종료 2 | 기존 backend `run_blender`는 비정상 종료를 모두 실패로 본다. 위 로컬 어댑터의 조건부 산출물 처리를 그대로 운영에 적용했다고 볼 수 없다. | 박재현 |
| 요청 중복 | 실제 `/character-models/{id}/rig`에 중복 방지 키/조회 계약이 없다. 응답 유실 후 자동 재전송은 안전하지 않다. Unity 실제 연결에서는 요청 불명확 상태를 보존하고 자동 재전송하지 않는다. | 장세찬·박재현 |
| 확정/습관 | #24 확정·현재 버전·설정 복원 API가 필요하다. 목 저장으로 실제 방 진입 제한을 우회하지 않는다. | 장세찬 |

## 목 서버와 검증

`unity/tools/rig-mock-server.py`는 127.0.0.1:8767에서만 듣는다. 가짜 `demo-access`만 사용하고 실제 계정 정보를 넣지 않는다. `--blender`에 로컬 4.2.0 실행 파일을 주면 준비 GLB와 제출 관절로 실행한다. 생략하면 고정 결과이며 `fixtureOnly:true`, `correctionApplied:false`로 구분한다.

목 전용 `quality`, `fixtureOnly`, `correctionApplied`를 실제 API의 확정 필드로 간주하지 않는다. 목은 준비된 GLB·관절의 공간을 맞추고, 내용 기반 중복 방지·작업 입력 저장·중단 재개·실패 후 재시도를 제공한다. `--joints-delay`는 409, `--rig-fail`은 작업 실패 검사용이다.

- `unity/tools/test-rig-mock.py -v`: 인증/형식/409, 준비 메시·중복 요청·품질 경고, 실패/재시도/복구 3개 묶음.
- `unity/tools/verify-rig-http.py`: 실제 로컬 Blender HTTP 검사. `Temp/batch/rig-http-smoke.json`을 먼저 읽어 중단된 조회부터 이어 간다.
- Unity `verify-rig-core.cs`: 엄격한 JSON, 복사/좌표, 저장 실패 시 전송 금지, 실제 연결의 모호한 요청 재전송 금지, 목 재개, 모델/사용자 분리, HTTP 재생·골반 이동의 루트 스케일 독립성 8개 묶음 통과.

목 상태·로그·생성 산출물은 무시되는 Unity `Temp/` 아래에 있다. 배포·운영 계정·외부 Gemini/Meshy 실행은 미실행이다.
