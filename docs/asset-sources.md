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
