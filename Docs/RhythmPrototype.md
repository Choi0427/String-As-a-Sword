# 검을 현 리듬 프로토타입

2026년 10월 4일 구현. Unity 6000.5.5f1에서 8×8 체스판 이동과 87 BPM 리듬 루프를 실행할 수 있다. 보스, 전투, 잡음 수집, HP, 탈출은 이번 범위에서 제외했다.

## 실행

1. `Assets/_Project/Scenes/RhythmPrototype.unity`를 연다. 메뉴 `String As a Sword > Open Rhythm Prototype`에서도 열 수 있다.
2. Unity의 Play를 누르고 Game 뷰를 클릭한다.
3. Space 또는 Enter, 화면의 Start 버튼으로 시작한다.
4. 네 박자 카운트인 후 좌우 음표가 흰 판정선에 닿을 때 WASD를 누른다.

| 입력 | 동작 |
|---|---|
| W / A / S / D | 박자 판정에 성공하면 위 / 왼쪽 / 아래 / 오른쪽으로 한 칸 이동 |
| Space / Enter | 시작 또는 정지 상태에서 새 세션 시작 |
| R | 위치, 콤보, 판정 수, 비트 및 오디오 예약을 초기화하고 재시작 |
| Esc | 세션 정지, 재시작 메뉴 표시 |
| M | 메트로놈 클릭 소리 켜기 / 끄기 |

시작 좌표는 (3,3), 화면의 D4다. 좌하단이 (0,0), 우상단이 (7,7)이다. 키를 계속 누르면 반복 이동하지 않으며 대각선 이동도 없다. 동일 시각의 방향 입력은 W → A → S → D 순서로 처리한다. 보드 밖 이동은 행동권을 소비하지 않아 같은 박자에 다른 방향으로 재시도할 수 있다.

## 적용 규칙

- 87 BPM, 1박 간격은 `60 / 87 = 약 0.689655초`. 매 박자 노트 하나를 생성한다.
- 좌우 음표는 동일한 beatId를 보여 주는 거울 표시다. 입력은 한 번만 필요하다.
- Perfect ±70ms, Good ±140ms. 창 밖 입력은 이동하지 않는다.
- 성공한 첫 합법 입력이 해당 박자의 행동권을 소비한다. 두 번째 입력으로 이동할 수 없다.
- 입력하지 않은 노트는 창이 닫힐 때 Miss로 한 번만 처리하고 콤보를 초기화한다. 창 밖 입력도 콤보를 초기화하지만 Miss 통계는 노트당 한 번만 센다.
- 리듬 시각은 `AudioSettings.dspTime` 기준이다. 화면 프레임 간격을 누적하여 비트를 계산하지 않는다.
- 포커스 상실, Esc, 2초를 넘는 프레임 정지에서는 오디오와 입력 처리를 정지한다. 이어하기 대신 새 카운트인으로 재시작한다.

기획서의 WASD 방향 선택 + L 실행 제안보다 이번 사용자의 WASD 직접 이동 요청을 우선했다. 성공한 입력 즉시 논리 위치를 변경하여 조작 피드백을 주며, 보스전의 판정창 종료 시 일괄 전투 해결은 아직 구현하지 않았다.

## 음원과 설정

`Assets/_Project/Data/Songs/GeomEulHyeon87.asset`을 선택하면 Inspector에서 설정할 수 있다.

현재 원곡 AudioClip은 제공되지 않아 직접 합성한 클릭음을 DSP 시각에 예약 재생한다. 이 상태에서는 노트가 계속 생성된다. 원곡을 자동 다운로드하거나 포함하지 않았다.

| 설정 | 의미 |
|---|---|
| Bpm | 고정 박자 속도, 기본 87 |
| Music | 사용할 AudioClip. 연결하면 카운트인 뒤 음악에 맞춰 실행하며 파일 종료 시 세션을 마친다 |
| Audio Start Offset | 파일 시작부터 첫 유효 박자까지의 초. 음악은 첫 노트보다 이 값만큼 먼저 재생된다 |
| Input Offset | 양수이면 늦게 기록된 입력을 판정상 앞당긴다. `보정 시각 = 입력 시각 - offset` |
| Visual Offset | 양수이면 음표 표시를 음악 시각보다 앞당긴다 |
| Perfect Window / Good Window | 판정 허용 초. Good은 비트 간격의 45% 이하로 제한한다 |
| Count In Beats | 준비 박자 수, 기본 4 |
| Note Approach Beats | 노트가 판정선까지 접근하는 박자 수, 기본 4 |

원곡의 실제 첫 박자 오프셋과 음원 전체 템포 변화는 이번에 검증하지 않았다. 원곡 연결 후 초반·중반·후반의 싱크를 확인해야 한다.

## 구조와 확장 지점

| 구성 | 책임과 후속 개발 방향 |
|---|---|
| BoardState / GridPosition | Unity 표시와 독립된 격자·경계 규칙. 추후 위험 칸과 ExitCell 규칙을 추가할 위치 |
| IBeatTimeline / ConstantBeatTimeline | 비트 시각 공급. 추후 저작한 비트맵 구현으로 교체 가능 |
| SongClock / BeatScheduler | DSP 시간과 누락 없는 비트 발행. 시간 공급자를 주입해 테스트 |
| RhythmJudge | 시각 보정과 판정. UI 및 오디오와 독립 |
| ActionGate | 비트당 한 행동. 추후 공격·방어 요청도 동일 게이트를 거치게 확장 |
| RhythmSession | 플레이어 위치, 콤보, 노트 통계 및 이동 결과 이벤트 |
| PlayerInputAdapter | Input System의 누름 이벤트와 원본 시각을 보존해 도메인 요청으로 변환 |
| PrototypeGame | 준비·카운트인·진행·정지·완료 상태 및 의존 객체 조립 |
| ScheduledSongAudio | 음악·메트로놈 예약 재생과 예약 취소 |
| PrototypeView / NotePresenter / PrototypeGlyph | HUD·보드·좌우 노트·교체 가능한 임시 나이트 그래픽 |
| SongDefinition | 변경 가능한 원본 설정. 세션 상태를 ScriptableObject에 저장하지 않음 |

추후 전투는 `ActionRequest`와 `CombatResolver`를 추가해 이동·공격·방어가 동일한 `ActionGate`를 공유하게 구성하면 된다. 잡음 수집은 승인된 이동 결과를 받아 처리하고, 화면은 도메인 이벤트를 구독하도록 유지한다. 현재 나이트와 음표는 코드 기반 임시 그래픽이며 아트 에셋으로 교체할 수 있다.

씬에는 조립용 객체와 설정 참조를 저장하며 UI는 실행 시 생성한다. 런타임, 에디터 도구, EditMode 테스트, PlayMode 테스트를 asmdef로 분리했다. TextMesh Pro 기본 리소스와 해당 라이선스도 저장소에 포함했다.

## 검증 결과

- EditMode 25개 통과: 네 방향 이동, 네 경계, 판정창 경계, 입력 보정, 고속 BPM 창 제한, 동일 박자 재입력, 무입력 Miss, 프레임 건너뛰기, 재시작, DSP 시계, 87 BPM 장기 시각 변환.
- PlayMode 3개 통과: 실제 Input System 키 이벤트를 박자마다 전달하여 네 방향 이동·홀드 반복 방지 확인, 동시 입력 우선순위·Space 시작·R 재시작·포커스 상실 정지, 좌우 노트와 나이트의 렌더링 메시 확인.
- 에디터 프레임이 실제 진행하는지 확인했으며, 순차 입력으로 8연속 콤보를 만든 시점도 확인했다.
- 1600×900 Game 뷰 캡처에서 체스판·나이트·좌우 노트·HUD를 확인했다. Esc 키로 정지 상태 전환도 확인했다.
- 최종 플레이 세션에서 컴파일 오류 및 런타임 콘솔 오류 0건을 확인했다.

Unity Test Runner에서 `StringAsASword.Tests`를 필터링해 재실행할 수 있다. 실행 파일 빌드와 실제 스피커·모니터의 지연 측정은 이번 검증에 포함하지 않았다.

## 기능별 커밋 메시지

1. `feat: add beat-gated chessboard movement and rhythm domain rules`
2. `feat: synchronize 87 BPM audio and timestamped keyboard input with DSP clock`
3. `feat: add playable rhythm prototype scene and mirrored note interface`
4. `docs: record rhythm prototype controls architecture and validation`

작업 시작 전에 존재하던 프로젝트 설정·렌더 파이프라인·튜토리얼 에셋 변경 및 삭제는 이번 기능 커밋에 포함하지 않았다.
