# 검을 현 GDD v0.6 프로토타입

2026년 10월 8일 구현. Unity 6000.5.5f1에서 기존 8×8 체스판 이동·87 BPM 리듬에 요청한 6개 기능을 추가했다. 첨부된 GDD v0.6을 참고하되, 최상단 밖 J도 허용하고 K는 동작만 구현하라는 사용자의 요청을 우선했다.

## 실행과 조작

1. `Assets/_Project/Scenes/RhythmPrototype.unity`를 연다. 메뉴 `String As a Sword > Open Rhythm Prototype`에서도 열 수 있다.
2. Play를 누르고 Game 뷰를 클릭한다.
3. Space 또는 Enter, 화면의 Start 버튼으로 시작한다.
4. 네 박자 카운트인 후 좌우 음표가 흰 판정선에 닿을 때 행동한다.

| 입력 | 동작 |
|---|---|
| W / A / S / D | Perfect 또는 Good이면 상 / 좌 / 하 / 우 한 칸 이동 |
| J | 모든 칸에서 검을 휘두름. 최상단 A8~H8에서만 보스 HP 피해 요청 |
| K | 방어 자세와 방패 윤곽 표시. 피해 감소·무적·홀딩·반사는 없음 |
| Space / Enter | 시작 또는 정지 상태에서 새 세션 시작 |
| R | 위치·HP·페이즈·잡음·난수·판정·예약 오디오를 초기화하고 재시작 |
| Esc | 세션 정지 및 재시작 메뉴 |
| M | 대체 메트로놈 클릭 소리 켜기 / 끄기 |

시작 위치는 D4=(3,3), 최상단은 y=7이다. 이동·공격·방어가 박자당 한 행동권을 공유한다. 입력 시각이 같으면 J → K → W → A → S → D 순서로 처리한다. 키 홀드로 동작을 반복하지 않는다. 보드 밖 이동은 거부하고 행동권을 소비하지 않는다. 최상단 밖 J는 허용된 헛스윙이므로 행동권을 소비한다.

## 추가한 기능

### 1. 잡음 생성·수집

- 전투 시작 비트에 1개, 이후 전역 비트 8·16·24…마다 1개 생성한다. 화면 표시는 1부터 시작하므로 비트 번호 표시와 내부 ID는 1 차이 난다.
- 플레이어 칸과 기존 잡음 칸을 제외한 유효 칸을 시드 난수로 선택한다.
- 보드에 최대 2개를 유지하며 상한에 걸린 생성 기회는 건너뛴다. 밀린 생성을 나중에 몰아서 처리하지 않는다.
- Good/Perfect 이동으로 논리적으로 착지하면 자동 수집한다. 공격·방어·대기로는 수집하지 않는다.
- 시간 소멸이 없다. 목표를 채우면 생성·추가 수집을 멈추고 남은 잡음은 흐리게 표시한다.
- 다음 페이즈에서 기존 잡음과 수집량을 초기화한다. Phase 2·3의 첫 잡음은 전환 다음 비트에 생성하며 정규 생성과 겹치면 한 번만 생성한다.
- 재시작은 시드와 세션 ID를 초기화한다. 같은 조건의 실행은 같은 배치를 재현한다.

### 2. 공격

J는 64칸 모두에서 실행할 수 있다. 실제 피해는 금색으로 표시된 A8~H8에서만 발생한다. Good과 Perfect 모두 기본 피해 1이다. 성공한 공격을 예약하고 해당 판정창이 닫힐 때 한 번만 적용한다.

### 3. HP 하한

보스 HP는 기본 6HP씩 4칸, 총 24HP다. 현재 칸이 0이 되면 추가 공격 피해가 0이 되며 다음 잠긴 칸으로 초과 피해를 넘기지 않는다. 현재 피해 하한은 순서대로 18 → 12 → 6 → 0HP다. `BossHealthGate`가 HP 변경을 담당한다.

### 4. 네 페이즈와 세 번의 주선율 탈취

| 페이즈 | 현재 HP | 잡음 목표 | 진행 |
|---|---|---|---|
| 1 | 첫 번째 6HP | 3개 | 두 조건 충족 시 2번 칸 개방 |
| 2 | 두 번째 6HP | 4개 | 두 조건 충족 시 3번 칸 개방 |
| 3 | 세 번째 6HP | 5개 | 두 조건 충족 시 4번 칸 개방 |
| 4 | 마지막 6HP | 없음 | 마지막 HP 소진 표시 |

체력과 수집의 완료 순서는 자유다. **현재 HP 칸 소진 AND 잡음 목표 충족**을 해당 비트 종료 시점에 검사하고 한 번만 전환한다. 해제 자체는 HP를 감소시키지 않는다. 음악·전역 비트·노트 흐름은 끊기지 않는다. 4페이즈에서는 잡음을 생성하지 않는다.

### 5. 2.5D 카메라

64개 칸과 판 받침을 실제 3D 메시로 만들었다. 카메라는 수평 기준 60도, 완전한 탑뷰에서 30도 기울어진 직교 시점이다. 말·잡음·검 궤적을 월드에 표시하고, 별도 RenderTexture를 통해 HUD와 독립적으로 렌더링한다. 화면 종횡비가 바뀌어도 보드 투영을 유지한다.

### 6. 방어 동작

K 성공 시 짧게 자세를 낮추고 푸른 방패 윤곽을 표시한다. 공격이나 이동과 같은 행동권을 사용하지만 위치·보스 HP·잡음 수집에 영향을 주지 않는다. 이후 보스 패턴을 연결할 수 있도록 액션 종류와 표시를 분리했다.

보스 패턴, 플레이어 피해·HP, 홀딩 반사, 사망·탈출·엔딩은 이번 구현 범위에 포함하지 않았다. 4칸을 모두 소진하면 완료를 HUD에 표시하며 음악은 계속 진행한다.

## 리듬·오디오

- 87 BPM, 한 박자 약 0.689655초, 매 박자 노트 하나.
- 좌우 음표는 같은 beatId의 거울 표시이므로 입력은 한 번만 필요하다.
- 기본 Perfect ±70ms, Good ±140ms. 창 밖 입력은 동작하지 않는다.
- DSP 절대 시각과 Input System의 원본 이벤트 시각을 사용한다.
- 기존의 즉각적인 논리 이동·착지 수집을 유지한다. 시각적 이동은 짧게 보간하며, 공격 피해와 페이즈 검사는 판정창 종료 시 정산한다.
- 무입력 노트는 창 종료 시 Miss로 한 번만 처리한다.
- 포커스 상실, Esc, 2초를 넘는 프레임 정지에서는 세션을 정지한다. 재개는 새 카운트인으로 재시작한다.

현재 로컬 프로젝트에 사용자가 연결한 `Strings as a Sword (검을 현).mp3`를 사용한다. 음원과 기존 SongDefinition 변경은 이번 커밋에 포함하지 않았다. 음원이 없으면 대체 클릭음을 예약 재생한다. 곡의 첫 유효 박자 오프셋과 전체 구간의 음악적 싱크는 별도 청음 조정이 필요하다.

## Inspector 설정

`Assets/_Project/Data/Stages/FourPhasePrototype.asset`:

| 설정 | 기본값 |
|---|---|
| Noise Seed | 87 |
| Noise Spawn Interval | 8비트 |
| Max Active Noise | 2 |
| Noise Targets | 3 / 4 / 5 |
| Health Segments | 6 / 6 / 6 / 6 |
| Attack Damage | 1 |
| Camera Elevation | 60도 |

`Assets/_Project/Data/Songs/GeomEulHyeon87.asset`에서 BPM, AudioClip, 첫 박자 오프셋, 입력·표시 오프셋, 판정창, 카운트인과 노트 접근 시간을 변경할 수 있다. 재생 상태는 설정 에셋에 기록하지 않는다.

## 객체 구조와 확장

| 구성 | 책임 |
|---|---|
| BoardState / GridPosition | 격자·경계·상하좌우 이동 |
| IBeatTimeline / SongClock / BeatScheduler | 교체 가능한 비트 시각, DSP 기준 진행, 비트 발행 |
| RhythmJudge / ActionGate / RhythmSession | 타이밍, 공통 행동권, 행동 결과·비트 종료 이벤트 |
| PlayerInputAdapter | 입력 이벤트 시각 보존 및 동시 입력 우선순위 |
| NoisePickupSystem | 생성·난수·상한·논리 수집·페이즈별 초기화 |
| AttackResolver | 공격 행 검사와 피해 요청 |
| BossHealthGate | 현재 칸 HP 하한·피해 제한·다음 칸 잠금 |
| CombatResolver | 승인된 공격 예약 및 비트 종료 시 피해 적용 |
| PhaseController | HP와 수집의 AND 조건, 3회 개방, 다음 페이즈 준비 |
| PrototypeGame | 객체 조립, 시작·정지·재시작, 비트·행동·전투 순서 |
| BoardWorldPresenter | 3D 보드·말·잡음·검·방어 동작과 카메라 |
| PrototypeView / NotePresenter | HUD와 고정 풀의 좌우 노트 |
| StageDefinition / SongDefinition | Inspector에서 수정하는 설정 |

논리 클래스는 Unity 장면·입력·표시 API에 의존하지 않는다. 추후 보스 패턴과 반사 피해를 `CombatResolver`에 연결하되 HP 변경은 `BossHealthGate`를 통과시키면 된다. 방어 판정은 Guard 행동에 별도 규칙을 추가할 수 있으며 현재 시각 효과가 게임 규칙을 결정하지 않는다.

보드·UI는 런타임 생성 방식이다. 잡음 표시는 설정 상한만큼 미리 만들고 재사용한다. 판정 시각은 프레임 시간 누적이 아니며, 저하된 프레임에서도 이전 비트 종료 → 다음 비트 생성 → 현재 입력 순서를 유지한다.

## 검증 결과

- EditMode **44개 통과**: 기존 리듬·이동·시계, 64칸 공격 범위, 공통 행동권, 시드·중복·상한·시간 유지·목표 수집, HP 하한·초과 피해, 두 조건의 완료 순서, 정확히 3번의 페이즈 전환, 재시작, 방어 무피해.
- PlayMode **6개 통과**: 실제 키 이벤트의 WASD 이동·홀드 방지, 동시 방향과 재시작, 노트 메시·64개 3D 칸·60도 카메라·RenderTexture, J/K/W 우선순위, 최상단 공격 7회 중 6HP만 감소, K 홀드·무피해, 실제 이동 착지 수집과 재시작 초기화.
- 1600×900 Game View에서 2.5D 보드, 잡음 파형, HP 잠금, K 방어 자세를 확인했다. 캡처: `Logs/Prototype/gdd06-final.png` (로컬 검증 자료, Git 제외).
- 최종 컴파일 오류 및 런타임 콘솔 오류 0건.
- Unity MCP 연결과 프로젝트 경로를 확인했고 Unity CLI를 **1.0.0-beta.13**으로 갱신했다.

Unity Test Runner에서 `StringAsASword.Tests`를 필터링해 재실행할 수 있다. 실행 파일 빌드, 기기별 성능 측정, 전체 곡의 음악적 싱크 청음은 이번 검증에 포함하지 않았다.

## 기능별 커밋

1. `78955f6` — `feat: add seeded noise spawning and automatic landing collection`
2. `ec6d3a7` — `feat: add rhythm-gated sword attacks with top-row damage targeting`
3. `846d5d6` — `feat: clamp boss damage to the active health segment floor`
4. `6076b7e` — `feat: unlock four phases through health and noise objectives`
5. `0e53b6a` — `feat: present the chessboard through a tilted 2.5D camera`
6. `feat: add visual-only guard actions and verify the combat loop`

작업 시작 전에 존재하던 프로젝트 설정·렌더 파이프라인·튜토리얼 에셋·음원 설정 변경과 파일 삭제는 기능 커밋에 포함하지 않았다.
