이 Unity 프로젝트에서 1페이즈 보스 모양 후보 48개가 검증기를 통과하는지, 그리고 인게임에서 설계대로 플레이되는지 Unity MCP로 검증해 주세요. 진행 설명과 최종 보고는 한국어로 해 주세요.

이 문서는 Unity가 없는 클라우드 세션에서 작성했습니다. 문서에 적힌 "최소 경고" 값은 Python으로 계산한 것이고, **실제 검증기와 인게임에서는 아직 한 번도 확인하지 않았습니다.** 이 작업으로 처음 확인하는 것입니다.

## 0. 시작 전에

1. 원격 브랜치 `claude/dreamy-lamport-dm1cxg`를 받아 그 위에서 작업하세요. 이 브랜치에 후보 파일과 설계 문서가 있습니다. 작업 트리에 커밋하지 않은 사용자 변경이 있으면 건드리지 말고 먼저 알려 주세요.
2. 프로젝트 지침(AGENTS.md, CLAUDE.md가 있으면)과 최근 커밋 형식을 확인하세요. 최근 커밋은 `docs: …`, `test: …`처럼 접두어 뒤에 한글로 간단히 씁니다.
3. 사용할 수 있으면 Unity 관련 스킬(`unity:unity-cli` 등)을 읽고 적용하세요. Unity MCP와 CLI가 이 프로젝트의 에디터(Unity **6000.5.5f1**)에 연결되어 있는지 먼저 확인하세요. 연결이 안 되면 그 사실과 원인을 알리고, 할 수 있는 단계만 진행하세요.
4. 에디터가 열려 있으면 배치 모드(`-batchmode`)로 같은 프로젝트를 열 수 없습니다. 검증은 열린 에디터에서 MCP로 하세요.

### 먼저 읽을 것

| 파일 | 왜 |
|---|---|
| `Docs/BossChart/phase1-candidates.bosschart.txt` | 검증 대상 48개 모양. 머리말에 표기 규칙과 예시 시퀀스가 있음 |
| `Docs/BossChart.md` | 0/1/2 형식, `anchor`, 공격 줄, 검증기 규칙 |
| `Docs/LyricBossDesign.md` 2·4·5절 | 노트 번호 지도, 구간별 의도, 최상단 규칙 |
| `Docs/BossCombat.md` "예고 표시와 실제 화면 검증" 절 | 화면 캡처 방법(`capture_game_view(source="screen")`, `camera`로 찍으면 HUD가 빠짐) |
| `Assets/_Project/Editor/BossChartValidator.cs` | 검증기. 메뉴 `String As a Sword/Boss Chart/Validate`, `Run(path, seeds)`, `RunFromCommandLine` |
| `Assets/_Project/Editor/BattleVisualVerification.cs` | 기존 화면 검증 fixture. `Prepare`/`PrepareResponses`/`Respond`/`Step`/`MoveStep`/`Show`/`Restore` |
| `Assets/_Project/Scripts/Combat/BossPatternSystem.cs` | 채보 모드 `PublishAuthored`, `AuthoredHits` |

### 꼭 알아야 할 사실 (코드로 확인함)

- 그림의 첫 줄이 판의 y=7, 즉 **최상단 공격 행**입니다. 나이트는 (3,3)=d4에서 시작하고, d4는 그림의 5번째 줄 4번째 칸입니다. `anchor=줄,칸`은 1부터 셉니다.
- `0` = 공격 없음, `1` = 일반(가드 또는 회피), `2` = 강공격(가드하면 `guard-broken`과 `player-hit`), 판 전체 `1` = 가드 전용.
- `anchor` 모양은 **공개 순간** 플레이어 위치로 가로·세로 모두 옮겨집니다. 판 밖으로 나간 칸은 잘리고 반대쪽은 채워지지 않습니다.
- 채보 모드는 연기나 재추첨 없이 적힌 노트대로 공개합니다. 공정성은 검증기가 미리 보장하는 구조입니다.
- 스테이지 연결은 `StageDefinition.bossChart`(TextAsset) → `BattleSettings.BossChartText`입니다. 코드에서는 `BattleSettings.From(song, stage).Copy()` 뒤에 `BossChartText`를 넣으면 **에셋을 저장하지 않고** 채보 모드로 돌릴 수 있습니다. `BattleVisualVerification.Prepare*`가 같은 방식(설정 복사 후 주입)을 씁니다.
- 로그 이벤트 이름: `pattern-publish`, `pattern-fire`, `pattern-skip`, `pattern-cancel`, `player-hit`, `guard-success`, `guard-broken`, `king-stunned`.
- 곡: RCM 채보 321노트(0부터 셈), 87 BPM. 빠른 구간(한 박보다 짧은 간격) 노트는 91–98, 201–208, 305, 306입니다. 피날레 결정 노트는 304입니다.
- `BossChart.md`와 `BossChart.cs` 주석은 첫 줄을 "플레이어가 시작하는 줄"이라고 쓰지만, 실제 시작은 (3,3)입니다. 고치지 말고 보고만 하세요.

## 1. 목표와 범위

48개 모양(맵 고정 27, 플레이어 추적 21)에 대해 다음 세 가지를 확인합니다.

1. **검증기**: 각 모양이 통과하는 최소 경고, 실패하면 그 원인.
2. **인게임 판정**: 예고 칸이 그림과 같은가, 카운트다운이 맞는가, 발동 노트가 맞는가. 그리고 `1` 칸 방어 성공, `2` 칸 방어 시 guard-broken+피격, 안전 칸 무피해, 가드 전용 판정이 설계대로 나오는가.
3. **화면 가독성**: 예고 칸과 숫자가 실제 화면에서 읽히는가(대표 장면 캡처).

범위 밖: 스테이지 에셋에 보스 채보를 영구 연결하는 것, battle-7 밸런스 확정, 게임 규칙 변경. 검증 중 버그를 찾으면 고치기 전에 보고하고, 수정은 별도 커밋으로 하세요.

## 2. 단계

### A. 검증용 채보 만들기

`Assets/_Project/Data/Bosses/Phase1Verification.bosschart.txt`를 만드세요.

- 후보 파일의 48개 모양을 그대로 복사하고, 각 모양을 **한 번씩** 공격하는 줄을 붙입니다.
- 경고는 후보 파일 주석의 "최소 경고"에 +1을 합니다(최소 1, 최대 5). 최소 경고가 0이면 1로 둡니다.
- 공격끼리 겹치지 않게, 다음 공격의 공개 노트가 앞 공격의 발동 노트보다 2 이상 뒤에 오게 배치합니다.
- 빠른 구간(91–98, 201–208)에 경고나 타격이 걸리지 않게 하고, 모든 타격은 300 이하로 둡니다.
- 모양마다 `section` 줄을 하나씩 달아 검증 보고서에 이름이 보이게 하세요(예: `section 폰줄-7 10-12`).
- 노트 번호 ↔ 모양 대응표를 결과 문서에 남깁니다.

### B. 검증기

1. 위 채보를 메뉴 **Validate**로 검사합니다(Project 창에서 파일을 선택한 뒤 실행). 결과는 `Docs/BossChart/validation.txt`에 저장됩니다. 기존 파일을 덮어쓰므로, 결과를 `Docs/BossChart/phase1-validation.txt`로 옮기고 원래 `validation.txt`는 되돌리세요.
2. 실패한 모양은 원인(피할 길 없음 / 빠른 구간 / 안전 칸 보호)과 출발 칸을 기록합니다.
3. **최소 경고 확정**: 모양마다 경고를 1부터 올려 가며 통과하는 최소값을 찾으세요. 모양 하나만 든 임시 채보로 반복하거나 `BossChartValidator.Check(text, settings)`를 에디터 C#으로 호출해도 됩니다. Python 계산값과 다르면 차이를 표에 남기세요.
4. **조합 검사**: 후보 파일 머리말의 예시 시퀀스를 노트 0–89에 실제 번호로 배치해 따로 검사하세요. 대상은 폰 전진 4연속, 비숍-흰칸·검은칸 교대, 오프닝 4연속, `전체 hits=2 every=2`, 핀입니다. `전체`는 시작용 채보 `GeomEulHyeon.bosschart.txt`에 있는 모양이니 복사해 오세요. 흰칸·검은칸 교대는 연속 노트 간격이 한 박인 곳에서만 성립합니다. 통과하지 못하면 통과하는 간격과 경고를 찾아 기록하세요.

### C. 인게임 플레이 검증 (Unity MCP)

스테이지 에셋을 저장하지 않는 방식으로 진행하세요.

1. Play 모드에서 쓸 Editor 전용 fixture를 만드세요. `BattleVisualVerification`처럼 `Assets/_Project/Editor/`에 `Phase1PatternVerification.cs` 정도로 두면 됩니다.
   - 설정 준비: `BattleSettings.From(game.Song, game.Stage).Copy()` → `BossChartText`에 A의 채보 텍스트를 넣습니다.
   - 방해 요소 제거: 잡음을 끄고(`SpawnInterval`을 크게), 연속 피격으로 죽지 않게 `PlayerMaxHp`를 크게 둡니다. 플레이어는 공격하지 않아 보스 HP가 줄지 않으므로 무력화(`Halt`)는 일어나지 않습니다.
   - 필요한 조작: 원하는 노트까지 진행, 플레이어를 특정 칸에 두기(실제 이동 입력으로), 한 노트에 가드/이동/대기, 현재 예고와 이벤트 출력.
   - 기존 fixture의 `BindBattle`/`Render`/`Restore` 방식을 재사용하고, 검증이 끝나면 원래 세션으로 복구되게 하세요.
2. 모양마다 아래를 확인하세요. 추적 모양은 플레이어 위치 **세 곳 이상**(중앙 d4, 가장자리 한 곳, 최상단 행 한 곳)에서 확인합니다.

   | 확인 | 방법 |
   |---|---|
   | 예고 칸 = 그림(추적은 옮긴 결과, 잘림 포함) | `pattern-publish`의 칸 마스크를 그림에서 계산한 기대값과 비교 |
   | 공개 노트 = 발동 노트 − 경고 | 이벤트의 노트 번호 |
   | 카운트다운 숫자 | 화면 또는 `Remaining` 값 |
   | `1` 칸에서 가드 → `guard-success`, 피해 없음 | Respond("guard") |
   | `2` 칸에서 가드 → `guard-broken` + `player-hit` | 강공격 모양만 |
   | 안전 칸에서 대기 → 이벤트 없음 | 대기 |
   | 위험 칸에서 대기 → `player-hit` 한 번 | 대기 |
   | 가드 전용(판 전체 `1`) → 어디서든 가드하면 성공 | `전체`를 쓰는 조합 검사에서 |
   | 0칸 회피 모양(킹고리·나이트훈수 계열) → 제자리 대기는 안전, 주변 칸으로 이동하면 피격 | 둘 다 |

3. 대표 장면을 `capture_game_view(source="screen")`로 캡처하세요. 장면은 맵 고정·추적 계열마다 1장씩, 강공격 1장, 가장자리에서 잘린 추적 모양 1장이고, 예고 숫자가 보이는 순간과 발동 X 순간을 담습니다. 파일은 `Docs/BossChart/phase1/`에 저장합니다. `camera`로 찍으면 HUD가 빠지니 `screen`을 쓰세요.

### D. 자동 회귀 테스트 (권장)

C의 판정 부분을 `Assets/_Project/Tests/PlayMode/` 또는 `EditMode/`에 테스트로 옮기세요. 화면 없이 `BattleSession`만으로 가능하면 EditMode가 낫습니다. 기존 `Tests/EditMode/BossChartTests.cs`의 방식(`BossChart.Parse`, `BossPatternSystem` 채보 모드)을 따르세요. 모양별 기대 칸 마스크, 발동 노트, 가드/대기 결과를 단언합니다.

### E. 결과 기록

- `Docs/BossChart/phase1-verification.md`
  - 모양별 표: 이름 / 종류 / Python 최소 경고 / 검증기 최소 경고 / 인게임 판정 / 캡처 / 문제
  - 노트 배치 대응표, 조합 검사 결과, 발견한 문제와 재현 방법
- `Docs/BossChart/phase1-candidates.bosschart.txt`
  - 각 모양 주석의 "최소 경고"를 검증기 값으로 바꿉니다.
  - 머리말의 "직접 계산한 값이라 Validate 결과가 우선해요" 문구를 검증 완료 문구로 바꿉니다.
  - 실패해서 쓸 수 없는 모양은 지우지 말고 주석에 이유를 적어 두세요.
- 커밋은 기능 단위로 나누고 한글로 간단히 씁니다. 예: `test: 1페이즈 보스 모양 인게임 검증 fixture 추가`, `docs: 1페이즈 보스 모양 검증 결과 기록`. 작업이 끝나면 `claude/dreamy-lamport-dm1cxg`에 push하세요.

## 3. 판정 기준과 보고

- **통과**: 검증기 통과 + 인게임 판정 표의 모든 항목이 기대와 같음.
- **실패**: 검증기 실패, 또는 인게임 결과가 그림·규칙과 다름. 재현 노트와 위치를 남깁니다.
- **보류**: 확인하지 못함. 이유를 남깁니다.
- 실행하지 않은 검증을 통과로 적지 마세요. MCP로 확인한 것과 추론한 것을 구분해 쓰세요.

최종 보고에는 다음을 넣어 주세요.

1. 통과 / 실패 / 보류 모양 수와 실패 목록
2. 최소 경고가 Python 값과 달라진 모양
3. 조합 검사 결과
4. '단계 / 커밋 해시 / 커밋 메시지 / 검증 결과' 표
5. 발견한 버그나 문서 오류(위의 "첫 줄" 표현 포함)

## 4. 알려진 위험

- 같은 노트에 공격 두 개는 검증기 **경고** 대상입니다. 배치할 때 피하세요.
- 추적 모양의 검증기 판정은 "같은 출발 칸 기준" 근사가 있습니다(`BossChart.md` "근사인 부분"). 그래서 인게임에서 직접 확인하는 것이 중요합니다.
- 2연속 노트(120틱)에서 판정창이 겹쳐 입력을 잃을 수 있습니다(`LyricBossDesign.md` 5절). 1페이즈 범위(노트 0–89)에는 해당 노트가 없지만, 입력 자동화 타이밍은 노트 시각 정각으로 두세요.
- 입력을 노트 시각 정각에 넣는 자동 검증은 사람의 타이밍 오차를 반영하지 않습니다. 가독성(예고를 읽고 반응할 시간)은 캡처를 보고 사람이 따로 판단해야 한다고 보고에 적어 주세요.
