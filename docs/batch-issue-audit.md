# 일괄 작업 커밋·이슈 판정

2026-10-31. 사용자 요청에 따라 구현 커밋 [8445ade4](https://github.com/2-changmin/withme/commit/8445ade4e0d44635d6df40fe708f9aa0ac2712fc)를 origin/unity에 푸시했다. main 병합·APK·운영 배포는 수행하지 않았다.

## 종료한 이슈

| 이슈 | 범위 | 검증 근거 |
|---|---|---|
| [#1](https://github.com/2-changmin/withme/issues/1) | 표준 33뼈·규격·skeleton.blend | [완료 조건·검사·한계](https://github.com/2-changmin/withme/issues/1#issuecomment-6537852679) |
| [#2](https://github.com/2-changmin/withme/issues/2) | 대기/걷기·체형 보정·animations.blend | [완료 조건·검사·한계](https://github.com/2-changmin/withme/issues/2#issuecomment-6537853718) |
| [#5](https://github.com/2-changmin/withme/issues/5) | 나머지 공용 클립 17개 세트 | [완료 조건·검사·한계](https://github.com/2-changmin/withme/issues/5#issuecomment-6537854847) |
| [#11](https://github.com/2-changmin/withme/issues/11) | 선택적 관절 보정 UI | [완료 조건·검사·한계](https://github.com/2-changmin/withme/issues/11#issuecomment-6537855812) |
| [#12](https://github.com/2-changmin/withme/issues/12) | 외형·확정 제한·4개 습관 UI | [완료 조건·검사·한계](https://github.com/2-changmin/withme/issues/12#issuecomment-6537856692) |
| [#13](https://github.com/2-changmin/withme/issues/13) | 방 5상태·기본 소리·단일 메뉴 | [완료 조건·검사·한계](https://github.com/2-changmin/withme/issues/13#issuecomment-6537857843) |

종료는 해당 이슈의 구현 조건 기준이다. 사용자 시각 승인·실제 서버 연동·기기 검증까지 통과했다고 표시하지 않았다. #11~13의 독립 검사는 PC 목 범위이며 품질 미승인 모델의 일반 확정/방 진입 제한은 유지한다.

## 유지한 이슈

| 이슈 | 남은 조건 |
|---|---|
| [#3](https://github.com/2-changmin/withme/issues/3) | 웨이트 실패 시 면 수 축소 후 재시도 구현이 없음. 모델 품질·입력 출처 확인도 남음 |
| [#4](https://github.com/2-changmin/withme/issues/4) | 3모델 중 최소 2개의 자동 관절 위치가 그럴듯한지 시각 확인 |
| [#7](https://github.com/2-changmin/withme/issues/7) | 기준 폰 실제 재생·fps |
| [#8](https://github.com/2-changmin/withme/issues/8) | 실제 이메일 코드 로그인·기기 보안 저장 |
| [#15](https://github.com/2-changmin/withme/issues/15) | 성능 30fps/150ms/5초 및 서명/파일럿 배포 구체화 |
| [#17](https://github.com/2-changmin/withme/issues/17) | 공동 관절 계약 합의·zod/packages/shared 문구와 실제 Pydantic 구조 정합성 |
| [#52](https://github.com/2-changmin/withme/issues/52) | 실제 전체 파이프라인→기준 폰·몸통 늘어남 판단·필요 시 전원 축소 결정 |

실제 확정·버전·습관 저장과 재접속은 [#24](https://github.com/2-changmin/withme/issues/24) 준비 후 연결한다. 사용자 확인 절차는 [검증 목록](../unity/docs/user-verification.md), 수치와 경고는 [인수 기록](batch-acceptance.md)에 정리했다.

기존 미추적 `blender/work-plan.md`는 이번 구현 커밋에 섞지 않고 그대로 보존했다. 자동 실행 예약은 외부/품질 대기 상태로 중지한 채 유지한다.
