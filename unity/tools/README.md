# 초기 설정 도구

일반 실행에는 이 도구가 필요하지 않다. 생성된 폰트·TMP 설정·검증 씬은 저장소에 포함한다.

`initialize-fonts.cs`는 `Assets/` 밖에 둔 에디터 초기 설정용 C# 코드다. 앱 빌드에 포함되지 않으며 기존 폰트 에셋·검증 씬을 덮어쓰지 않는다. Unity 공식 CLI가 연결된 상태에서 최초 생성 시 사용한다.

```powershell
unity command script eval --code 'TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);' --caller plugin --skill ui-ugui --project-path ./unity/withme_frontend
# 필수 리소스 임포트가 끝난 뒤 실행
unity command script run --file ../tools/initialize-fonts.cs --entry WithmeSetup.InitializeFonts.Run --caller plugin --skill optimize-text-mesh-pro --project-path ./unity/withme_frontend
```

Unity CLI가 PATH에 없다면 설치된 `unity.exe`의 절대 경로를 사용한다. `font-characters.txt`는 와이어프레임 문구·기본 Latin·검증 이름의 378자다. 이후 문구 변경 시 필요한 글자를 추가하고 기존 폰트 에셋의 GUID를 유지하며 갱신한다. 새 PC에서 폰트를 다시 생성할 필요는 없다.

## #7 캐릭터 재생 검증

- `create-character-probe.cs` / `CreateCharacterProbe.Run`: 최초 검증 씬·공용 Walk 에셋·Controller 생성. 기존 씬은 덮어쓰지 않는다.
- `select-validation-view.cs` / `SelectValidationView.Run`: 현재 대상의 에디터 Game View를 390 × 844로 선택한다.
- `verify-character-probe.cs` / `VerifyCharacterProbe.Run`: Play 중 실제 뼈·스킨 변형, 공용/내장 걷기 비교, 자세 복귀, 잘못된 파일·뼈대, 재시도·중복 로드·UI 연결을 검사한다.
- `character-probe-build-settings.cs`: `Prepare`로 로컬 검증용 식별자·세로 방향·디버그 서명을 임시 적용하고 `Restore`로 원복한다. 원본 값은 에디터 SessionState에 보존하므로 Prepare 이후 Restore 전에는 에디터를 종료하지 않는다. 빌드 실패 때도 Restore를 실행한다.

실행 형식·결과·미완료 조건은 [#7 작업 기록](../docs/issues/U02-issue-7-character-playback.md)을 따른다. 이 도구들은 Assets 밖에 있어 플레이어 빌드에 포함되지 않는다.

검증용 Android 개발 APK는 도구·라이선스를 준비하고 Play Mode를 종료한 뒤 다음 순서로 만든다. 두 번째 명령이 queued를 반환하면 `build status`에서 completed와 성공 여부를 확인한다. APK는 첫 검증 씬만 포함하며 제품 배포용 서명을 사용하지 않는다.

```powershell
unity command script run --caller plugin --skill unity-cli --project-path ./unity/withme_frontend -- --file ../tools/character-probe-build-settings.cs --entry CharacterProbeBuildSettings.Prepare
unity command build run --caller plugin --skill unity-cli --project-path ./unity/withme_frontend -- --target Android --outputPath Builds/Android/issue7/withme-character-probe.apk --options '["Development","DetailedBuildReport"]' --scenes '["Assets/Scenes/CharacterPlaybackValidation.unity"]' --confirm true
unity command build status --caller plugin --skill unity-cli --project-path ./unity/withme_frontend
# 빌드가 완료되거나 실패한 뒤 원복
unity command script run --caller plugin --skill unity-cli --project-path ./unity/withme_frontend -- --file ../tools/character-probe-build-settings.cs --entry CharacterProbeBuildSettings.Restore
```

## #8 로그인·통신 검증

- `create-login-scene.cs` / `CreateLoginScene.Run`: 최초 제품 Start 씬·연결 설정 생성. 기존 씬은 덮어쓰지 않는다.
- `verify-auth.cs` / `VerifyAuth.Run`: 통신·세션·갱신 경쟁·저장 실패·401 재요청 상한을 목으로 검사한다. Play Mode가 필요 없다.
- `verify-login-view.cs` / `VerifyLoginView.Run`: Start 씬 Play 중 실행. 실제 임시 Input System 포인터로 화면 흐름을 검사한다. 테스트 전송을 주입하며 실제 메일을 보내지 않는다. 종료 시 입력 설정·장치를 복원한다. 테스트가 끝난 Play 세션은 목 모드이므로 실제 메일 로그인 전에 Play를 종료하고 다시 시작한다.
- `verify-auth-live.cs` / `VerifyAuthLive.Run`: 실제 배포 서버의 `/health`와 미인증 `/pets`만 GET으로 조회한다. 이메일·토큰·사용자 자료를 보내지 않는다.

실행 형식은 `unity command script run --caller plugin --skill unity-cli --project-path ./unity/withme_frontend -- --file ../tools/verify-auth.cs --entry VerifyAuth.Run`이며 UI 검증은 파일·entry를 해당 도구로 바꾼다. 결과·실제 로그인 방법·모바일 미검증 범위는 [#8 기록](../docs/issues/U03-issue-8-auth-plan.md)을 따른다.
