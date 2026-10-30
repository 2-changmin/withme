# 에셋·폰트 출처

## Noto Sans KR Regular

- 용도: Unity UI 한글 기본 폰트와 사용자 이름용 동적 fallback.
- 출처: [Noto CJK 공식 저장소](https://github.com/notofonts/noto-cjk/tree/main/Sans/SubsetOTF/KR).
- 파일: `unity/withme_frontend/Assets/Fonts/NotoSansKR/NotoSansKR-Regular.otf`.
- 라이선스: SIL Open Font License 1.1. 같은 폴더의 `OFL.txt`를 유지한다.
- 원본 파일은 수정하지 않았고 TMP 에셋만 생성했다. 다운로드 날짜·원본 SHA-256은 같은 폴더 `SOURCE.md`에 기록했다.

## TMP Essential Resources

- 출처: 프로젝트에 고정된 Unity UI `com.unity.ugui@2.0.0`의 TMP Essential Resources.
- 가져온 범위: 필수 리소스만 포함, Examples & Extras는 제외.
- Liberation Sans: `unity/withme_frontend/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`와 원본을 함께 유지한다.
- EmojiOne: `unity/withme_frontend/Assets/TextMesh Pro/Sprites/EmojiOne Attribution.txt`를 함께 유지한다. 기본 리소스에 포함된 출처 기록이며 제품에서 이모지 사용을 확정한 것은 아니다.
- 셰이더·설정·기본 리소스는 가져온 패키지와 함께 제공되는 조건을 따른다.

## Unity 기본 템플릿

기존 URP 프로젝트의 SampleScene·Settings·TutorialInfo를 유지했다. 이번 초기 설정에서 제3자 강아지 모델·애니메이션·상용 UI 에셋을 추가하지 않았다. 이후 외부 에셋을 추가할 때는 출처·라이선스·사용 범위를 여기에 기록한다.

## #7 사용자 제공 강아지 검증 샘플

- 수령일: 2026-10-29. 사용자 첨부 `meshy_result+walk.zip`의 `01a18145_rigged_walk.glb`.
- 용도: 런타임 GLB 로드·공용 걷기·Android 개발 검증. 제품용 최종 모델이나 표준 뼈대 확정본이 아니다.
- 파일: `unity/withme_frontend/Assets/Validation/Character/Source/DogWalk.glb`, 동일 바이트의 실행용 `Assets/StreamingAssets/Validation/DogWalk.glb`.
- 원본 SHA-256: `58c96215009bd871cce4205ce12e74b06a68794ace3d9aa150c86b6694256aba`. GLB 원본은 변경하지 않았다.
- 파생물: 원본 Walk에서 추출한 SampleSharedWalk.anim과 빌드용 재질 참조. 새 대기 동작·음원은 추가하지 않았다.
- 제공 정보상 Meshy 모델과 Blender 리깅·걷기 결과이며 제작자·별도 라이선스 문서는 ZIP에 없다. 공개 재배포·제품 최종 에셋 채택 전에 제작자·이용 조건을 확인한다.

## 기본 강아지 효과음

- 수령일: 2026-10-30. 보호자 녹음 분할(sound_split) 대신 앱에 기본 소리를 넣어 동작별로 돌려 쓴다.
- 파일: `unity/withme_frontend/Assets/Resources/Sounds/Dog/` (`Resources.Load<AudioClip>("Sounds/Dog/greet_01")`).
- 동작별 파일: 반기기 `greet_01`·`greet_02`, 쓰다듬기 `pet_01`·`pet_02`, 공 쫓기 `fetch_01`, 대기 `idle_01`(반복 재생용).
- 생활 소리 `life/`: 킁킁 `sniff_01`·`sniff_02`, 핥기 `lick_01`, 하품 `yawn_01`·`yawn_02`, 몸 털기 `shake_01`·`shake_02`, 발소리 `steps_01`~`steps_03`. 동작 사이에 무작위로 튼다.
- 크기별 짖음 `bark_size/`: 중형 `medium_01`·`medium_02`, 대형 `large_01`·`large_02`. 소형견은 `greet_01`·`greet_02`·`fetch_01`을 쓴다.
- 출처: Freesound. 모두 CC0 1.0(퍼블릭 도메인)이라 수정·상업적 이용이 가능하고 출처 표시는 필요 없다. 원본 링크·사용 구간은 같은 폴더 `SOURCE.md`에 기록했다.
- 가공: 무음 제거, 음량 통일(동작 소리 -18 LUFS, 생활 소리 -20 LUFS·최대 4초), 모노 ogg(44.1kHz) 변환.

## NativeGallery — #9 사진 선택

- 출처: [yasirkula/UnityNativeGallery](https://github.com/yasirkula/UnityNativeGallery), MIT License. 저작권·라이선스는 패키지의 `LICENSE`를 유지한다.
- 버전: 1.9.5, 커밋 `6fdfae30629814b22b971453d87665d10e1c2ed4` 고정. UPM ID `com.yasirkula.nativegallery`.
- 범위: Android/iOS 사진 선택과 축소 미리보기. PC 에디터는 Unity 파일 선택창을 사용한다. 패키지 설치·컴파일은 확인했으며 기기 권한·포맷 지원은 미검증이다.
