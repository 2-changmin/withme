# 초기 설정 도구

일반 실행에는 이 도구가 필요하지 않다. 생성된 폰트·TMP 설정·검증 씬은 저장소에 포함한다.

`initialize-fonts.cs`는 `Assets/` 밖에 둔 에디터 초기 설정용 C# 코드다. 앱 빌드에 포함되지 않으며 기존 폰트 에셋·검증 씬을 덮어쓰지 않는다. Unity 공식 CLI가 연결된 상태에서 최초 생성 시 사용한다.

```powershell
unity command script eval --code 'TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);' --caller plugin --skill ui-ugui --project-path ./unity/withme_frontend
# 필수 리소스 임포트가 끝난 뒤 실행
unity command script run --file ../tools/initialize-fonts.cs --entry WithmeSetup.InitializeFonts.Run --caller plugin --skill optimize-text-mesh-pro --project-path ./unity/withme_frontend
```

Unity CLI가 PATH에 없다면 설치된 `unity.exe`의 절대 경로를 사용한다. `font-characters.txt`는 와이어프레임 문구·기본 Latin·검증 이름의 378자다. 이후 문구 변경 시 필요한 글자를 추가하고 기존 폰트 에셋의 GUID를 유지하며 갱신한다. 새 PC에서 폰트를 다시 생성할 필요는 없다.
