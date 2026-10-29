# Noto Sans KR

- 원본: Noto CJK 프로젝트의 한국어 서브셋 Regular, 수정하지 않은 OTF.
- 폰트: https://github.com/notofonts/noto-cjk/blob/main/Sans/SubsetOTF/KR/NotoSansKR-Regular.otf
- 라이선스: https://github.com/notofonts/noto-cjk/blob/main/Sans/LICENSE
- 받은 날짜: 2026-10-29
- 원본 SHA-256: `69975A0AC8472717870AEFEAB0A4D52739308D90856B9955313B2AD5E0148D68`
- 배포 시 `OFL.txt`를 함께 유지한다.

`NotoSansKR-UI.asset`은 UI 문구를 미리 구운 기본 폰트다. `NotoSansKR-Dynamic.asset`은 사용자 이름 등 추가 한글을 위한 동적 fallback이다. 두 에셋 모두 sampling 48 / padding 5 / scale 1 / atlas 1024로 생성한다. 동적 폰트의 Clear Dynamic Data On Build를 활성화한다.

UI 문구가 바뀌어 글자를 추가해야 할 때는 `unity/tools/font-characters.txt`를 갱신한다. 에셋을 무조건 다시 생성해 GUID를 바꾸지 말고 기존 폰트 에셋을 에디터 API로 갱신한다.
