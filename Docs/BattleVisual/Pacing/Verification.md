# battle-3 실제 Game View 확인

2026-10-23, Unity 6000.5.5f1, 1600×900, 프로젝트의 기존 60도 보드 카메라와 RenderTexture/uGUI 경로. 실제 PlayerLoop frameCount 1007→4547 증가를 확인한 뒤 캡처했다. 화면은 실제 BattleSession을 공개 정보 MovementUse 전략으로 진행해 찾은 장면이며, 장면 선택 이후 표시 시각만 고정한 Editor fixture이다. 사람이 실시간 반응한 시험은 아니다.

| 장면 | 실제 상태 | 파일 |
|---|---|---|
| 3노트 예고 | seed 0, phase 3, 완료 인덱스 98, 플레이어 A8, Top 범위 A8~D8, 발동 101 | [3](random-3.png) |
| Miss 후 2, 1 | 완료 인덱스 99, 100, 범위·마감 유지 | [2](random-2.png), [1](random-1.png) |
| 공격 발동 | 완료 101, X와 IMPACT, HP3→2 | [발동](random-impact.png) |
| 열 공격과 잡음 | seed 11, 완료 91, Column C, 플레이어 C8, 잡음 C3/A2 | [열](random-line.png) |
| 실제 회피 이동 중 | 같은 세션 C8→B8, 인정된 이동의 0.065초 보간 장면 | [이동](random-moving.png) |
| 처음부터 1노트 위치 고정 | seed 0, 완료 11, 플레이어 B8, Lock 발동 12 | [Lock](random-lock.png) |
| 고정 모드 중첩 회귀 | 기존 fixture seed 2, 행4의 3과 열D의 4, 플레이어 D4의 3+1 및 옆으로 배치된 4 | [중첩](fixed-overlap.png) |

8개 PNG를 직접 확인했다. 밝은 칸·어두운 칸 모두 어두운 받침 위 흰 빗금과 테두리가 식별된다. 플레이어가 서 있는 위험 칸에서는 숫자가 말 모양 아래에 보이며 이동 중에도 실루엣이 구분된다. C3 잡음의 청록색 파형과 흰 빗금·숫자 2가 같이 읽힌다. 3→2→1은 정확히 판정 종료 한 번마다 바뀌며 발동은 숫자 대신 X/IMPACT이다. 새 전역 모드 자체는 간격≥4/예고≤3으로 중첩이 생기지 않으므로 중첩은 기존 고정 모드 fixture로 확인했다.

`PrepareRandom(1/2/3)`, `Step()`, `MoveStep()`, `Restore()`로 재현한다. fixture는 설정을 복사하며 씬/Stage를 저장하지 않는다. Restore 후 실제 Stage의 Schedule.Enabled=true를 다시 확인하고 Play Mode를 종료했다. 현재 Unity Console ground truth 오류·경고는 0이었다(도구 버퍼에는 이전 컴파일/실패 테스트 이력이 남음).

사람의 1노트 반응 시간, 다른 해상도·화면 크기·대비 환경, 실제 곡 전체의 재미는 미검증이다.
