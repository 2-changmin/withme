# U01 — Unity CLI·MCP 연결

- 날짜: 2026-10-28
- 상태: 요청한 연결 설정·검증 완료. U01의 나머지 패키지·폰트·Git·LFS 항목은 미검증.
- 작업 트리: main의 미커밋 로컬 작업. 기존 `unity/`는 Git 미추적 상태였으며 커밋·푸시는 하지 않았다.
- 범위: 사용자 요청에 따라 Unity 공식 플러그인에서 에디터를 사용할 수 있도록 CLI·Pipeline·MCP 설정.

## 설정 결과

- 공식 플러그인: `unity@unity-agent-plugin` / `0.1.8-beta`.
- CLI: 공식 `https://unity.com/install.ps1`로 설치, 다운로드 SHA-256 검증 성공. `1.0.0-beta.14`이며 self-update 검사에서도 같은 최신 버전 확인.
- CLI 경로: `C:\Users\rksek\AppData\Local\Unity\bin\unity.exe`. 사용자 PATH에 추가됨.
- 프로젝트: `D:\001_ProjectFiles\008_withme\unity\withme_frontend`.
- Unity: 기존 `6000.0.76f1` 유지.
- `unity pipeline install`로 `com.unity.pipeline@0.9.1-exp.1` 추가. Package Manager가 `packages-lock.json`의 의존성을 해결함.
- Codex: `C:\Users\rksek\.codex\config.toml`의 `mcp_servers.unity` 등록. CLI 절대 경로와 프로젝트 경로 사용. 샌드박스 설정은 변경하지 않음.

## MCP 베타 이름 충돌 대응

기본 MCP 목록은 도구 166개를 반환했으나 그룹 이름을 뺀 `status` 등 이름이 중복됐다. 실제 `status` 호출은 `400 Bad Request: Command Not Found`와 함께 `editor status` 등 전체 명령을 요구했다.

공식 `--exclude-tools` 옵션으로 다음 중복 이름을 제외했다. 최종 도구 89개, 중복 이름 0개 확인. 제외된 기능은 CLI에서 `unity command editor status`처럼 전체 이름으로 사용할 수 있다.

```text
add,bake,bake_status,cancel_bake,clear_bake,create,delete,find,get,get_properties,get_settings,list,remove,rename,run,search,set,set_active,set_properties,set_settings,status
```

최종 MCP 실행 인자는 `mcp --project-path D:\001_ProjectFiles\008_withme\unity\withme_frontend --exclude-tools <위 목록>`이다. CLI·Pipeline 업데이트 후 중복 이름 문제가 해결됐는지 재검증한 뒤 제외 목록을 변경한다.

## 검증 기록

| 검증 | 결과 |
|---|---|
| 패키지 컴파일 | Editor.log에서 `Tundra build success`, 이후 에디터 `compiling: false`, `domainReloadInProgress: false` 확인 |
| CLI `status` | 해당 프로젝트, Unity `6000.0.76f1`, `ready`, 로컬 포트 `7800` 확인 |
| CLI `command editor status` | 실제 요청 성공, `ready`, Play Mode `stopped` |
| CLI `logs --console --level error --tail 10` | 성공, 결과 `[]` |
| MCP initialize / tools/list | `unity-mcp` / `1.0.0-beta.14`, 최종 도구 89개, 중복 없음 |
| MCP tools/call `eval` | 읽기 전용 C# 조회 성공, diagnostics `[]`, 프로젝트 Assets 경로·Unity 버전·`Assets/Scenes/SampleScene.unity` 반환 |
| Codex 현재 채팅에 MCP 도구 재로드 | 미검증. 새 세션에서 설정을 로드해야 함. 별도 MCP 클라이언트로 동일 실행 인자의 통신은 검증함 |
| 기능 테스트·APK·실기기 | 연결 설정 범위 밖으로 미실행 |

처음에는 에디터가 비활성 상태여서 `STATUS_PIPELINE_LOAD_PENDING`이었다. Unity 창 활성화 요청은 타임아웃됐지만 패키지 로딩이 시작됐고 컴파일·도메인 재로딩 후 `ready`로 전환됐다. 씬·프리팹·게임 오브젝트는 수정하지 않았다.

## 재확인

Unity에서 이 프로젝트를 연 상태로 다음을 실행한다. 제한된 셸에서는 사용자 계정의 에디터 탐색·로컬 통신 권한이 필요할 수 있다.

```powershell
& 'C:\Users\rksek\AppData\Local\Unity\bin\unity.exe' status --project-path 'D:\001_ProjectFiles\008_withme\unity\withme_frontend' --format json
& 'C:\Users\rksek\AppData\Local\Unity\bin\unity.exe' command editor status --caller plugin --skill unity-cli --project-path 'D:\001_ProjectFiles\008_withme\unity\withme_frontend' --format json
```

사용자 시각·조작 확인은 미확인이다. 다음 작업은 U01의 남은 항목 점검이다.
