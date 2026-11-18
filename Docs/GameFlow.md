# 전체 흐름: 메인화면 → 스토리 → 전투 → 엔딩 → 흰 메인화면

2026-11-08. 전투 설정 battle-6 (보스 HP 35/8/14/54, 측정은 [ChartBalance.md](ChartBalance.md)의 battle-6 절).

## 흐름

| 단계 | 씬 | 담당 | 내용 |
| --- | --- | --- | --- |
| 메인화면 | `MainMenu` | `MainMenuView` | 피그마 `Main Menu`(node 2:3) 그대로. 시작 / 설정 / 종료 |
| 스토리 | `MainMenu` | `MainMenuView` | 자막 카드. Space·Enter·클릭 다음, Esc 전부 건너뛰기. 시작할 때마다 나온다 |
| 전투 | `RhythmPrototype` | `PrototypeGame` | 메뉴에서 오면 준비 화면 없이 바로 카운트인 |
| 무력화 | 〃 | `BattleSession` + `FinaleDirector` | 보스 HP를 다 깎으면 킹이 흔들리고 공격·예고가 멈춘다 |
| 체스판 균열 | 〃 | `BoardWorldPresenter` | 보스 채보의 `싸비1` 구간이 끝나면(123번) 금이 가고, `싸비2`가 끝나면(233번) 금이 늘며 몇 칸이 내려앉고 돌조각이 흩어진다. 보스 HP 페이즈와는 무관하다 |
| 격파 | 〃 | `BattleSession` + `FinaleDirector` | 곡 끝 16분 3연노트(304~306번)가 시작될 때 승리 확정. 3연타마다 섬광, 이후 킹이 쓰러지고 곡이 끝날 때까지 체스판 가운데가 갈라진다. 가운데 6×6칸은 크기가 제각각인 각진 파편(얇은 판)으로 잘려 있다. 파편 사이 틈으로 아래의 하얀 빛이 보이고, 균열마다 빛의 막이 가운데부터 차례로 솟아 바깥으로 기울어 뻗으며(곡 끝으로 갈수록 높아진다), 빛나는 금이 가운데에서 보드 끝까지 뻗는다. 가운데 파편은 들리고 기울며, 가장 안쪽 4개는 빠져 작은 구멍이 열린다 |
| 마무리 | 〃 | 〃 | 남은 14노트는 계속 흐르지만 판정·Miss·페널티 없음 |
| 하얀 세계 | 〃 | `FinaleDirector` | 곡이 끝나면 가운데가 터진다(2초). 파편이 회전하며 바깥과 카메라 쪽으로 날아오고, 구멍에서 빛줄기가 뻗고, 흰 빛이 구멍 위치에서 원형으로 퍼져 화면을 덮는다. 이후 전부 하얀 화면에 나이트와 잡음 하나만 남는다. WASD·방향키로 격자 없이 이동(대각선도 같은 속도), 시간 제한 없음. 잡음은 나이트가 서 있던 곳에서 가장 먼 모서리에 있고, 가까울수록 지직거리는 소리만 커진다 |
| 컷신 | 〃 | `CutscenePlayer` | 잡음에 닿으면 검정 띠가 위아래에서 1.2초 동안 들어와 2.39:1이 되고 `ArtSet.endingCutscene` 영상이 재생된다(비어 있으면 검정 화면 2초). Space·Enter·Esc로 건너뛴다. 끝나면 흰색으로 덮고 클리어 저장 → 메인화면 |
| 흰 메인화면 | `MainMenu` | `MainMenuView` | 체스판·나이트는 그대로, 왼쪽(여백·로고·메뉴)만 반전되어 흰색 |

3연노트까지 HP를 다 깎지 못하면 실패(시간 초과)다. 실패하면 Game Over 화면, Esc는 Pause 화면이다(아래 "전투 화면").

## 규칙 (battle-6)

- `SongDefinition.finishNote`(Inspector): 마무리 연타의 첫 노트. 현재 304. -1이면 이전 규칙(HP 0 즉시 승리, 마지막 노트 후 시간 초과).
- `BattleSettings.FinishNote` / `DecidingNote`(= FinishNote - 1): 이 노트가 닫힐 때 무력화 상태면 승리, 아니면 실패.
- 무력화 순간 `BossPatternSystem.Halt()`가 공개된 예고를 취소하고 이후 공격을 내지 않는다. 로그 이벤트 `king-stunned`.
- 봇 측정의 "남은 노트"는 이제 무력화부터 결정 노트까지의 여유다.

## 메인화면

- 배경: `Art/UI/Menu/MainMenu_Logo_BG.png`(피그마에서 받은 3840×2160) → `ArtSet.mainMenuDark`.
- 글자: 80px, `#D9D9D9`, 자간 -3.2px, 왼쪽 184.5px / 위 631·733·835px. 호버: `#929292` 상자 + `#0C0C0C` 글자. 하단 "Inspired by 이승윤 <검을 현>" 16px `#929292`.
- 폰트: 학교안심 곧은제목 B(`Art/Fonts/HakgyoansimGodeunjemokB.ttf`)로 만든 동적 TMP 폰트 에셋 `HakgyoansimGodeunjemokB SDF`가 `ArtSet.menuFont`에 지정되어 있다. 메인화면, 스토리 카드, 컷신 건너뛰기 안내가 이 폰트를 쓴다. `String As a Sword > Art > Set Up Menu Font`를 실행하면 TTF에서 에셋을 다시 만들고 다시 지정한다. 슬롯이 비면 실행 중 OS 한글 폰트(맑은 고딕)를 동적으로 쓴다.
- 흰 버전: `Art/Shaders/MenuInvertLeft.shader`가 이미지 왼쪽만 색을 반전한다. 경계는 아래 36% → 위 45%로 기울고 3.5% 번진다(머티리얼 `Art/Materials/MenuInvertLeft.mat`에서 조정). `ArtSet.mainMenuWhite`에 손으로 만든 흰 이미지를 넣으면 그것을 쓴다.
- 클리어 기록은 저장되어 다음 실행에도 흰색이다. 메인화면에서 **Shift+R**로 되돌린다.
- 설정: 음악 볼륨(0~100%), 입력 싱크·화면 싱크(±200ms). 저장되며 곡 에셋 값 위에 더해진다.
- 키보드: W/S·방향키로 고르고 Enter·Space로 누른다.

## 전투 화면 (키 가이드 · Pause · Game Over)

피그마 `Game Play`의 `키 가이드`(node 373:184), `Pause`(389:255), `Game Over`(389:239)를 `BattleScreens`가 1920×1080 좌표 그대로 만든다(전투 캔버스 1600×900에 0.833배). 게임 화면이 뒤에 그대로 보인다.

- 키 가이드: `Art/UI/HUD/KeyGuide.png`(피그마 2배 내보내기, 검은 배경을 알파로 바꿈) → `ArtSet.keyGuide`. 보드 아래 (598, 939) 723×104. 에필로그(하얀 세계)에서는 숨는다. 카운트인 "GET READY"는 보드 가운데로 옮겼고, 개발자 모드 표시는 왼쪽 아래.
- Pause(Esc, 창 포커스 잃음): PAUSED / 판으로 복귀 / 설정 / 메인 메뉴. Esc를 다시 누르면 복귀(설정이 열려 있으면 설정만 닫힘). 복귀하면 멈춘 곳보다 카운트인 길이(4박)만큼 앞에서 곡이 다시 흐르고 "GET READY" 숫자가 멈춘 곳까지 센다. 이 구간의 노트는 이미 판정된 것이라 다시 판정하지 않는다.
- Game Over(플레이어 HP 0 또는 시간 초과): FALLEN / 첫 수부터 / 메인 메뉴. 첫 수부터는 새 판(4박 카운트인). 설정에서 입력 싱크를 바꿨다면 판정기를 새로 만들려고 씬을 다시 연다. R은 여전히 바로 재시작.
- 조작: 마우스 호버·클릭, 또는 W/S·방향키로 고르고 Enter·Space. 호버 모양은 메인화면과 같다(`#929292` 상자 + `#0C0C0C` 글자).
- 설정: 메인화면과 같은 `SettingsPanel`. 볼륨·화면 싱크는 복귀하면 바로, 체력 표시는 즉시, 개발자 무적·보스 HP는 닫을 때, 시작 페이즈는 첫 수부터에서 적용된다.
- 폰트: 항목 Orbit Regular 65px 자간 -10%(`Art/Fonts/Orbit-Regular.ttf`, OFL), 제목 Media Sans Extended Black 80px(`Art/Fonts/MediaSansExtended-Black.otf`). `String As a Sword > Art > Set Up Battle Screens`가 두 TMP 폰트 에셋을 만들고 키 가이드와 함께 `ArtSet.screenFont`·`screenTitleFont`·`keyGuide`에 지정한다. **Media Sans는 체험판(Trial) 라이선스**라 배포 전 정식 라이선스를 사거나 다른 폰트로 바꿔야 한다.
- 메인화면 없이 전투 씬만 실행하면 예전 준비 카드(START / QUIT)가 먼저 뜬다.
- 캡처: [BattleVisual/Screens](BattleVisual/Screens/). 프로젝트가 선형(Linear) 색 공간이라 피그마의 반투명(검정 90%, 키 상자 30%)은 알파를 감마 보정해 같은 밝기로 맞췄다.

## 스토리 문구 수정

`Data/Story/IntroStory.asset`의 카드 목록. 카드마다 문장과 선택 그림(`image`). 현재 문구는 GDD 콘셉트(잡음·주선율·흑백 체스판)로 쓴 초안이다.

## 씬 다시 만들기

`String As a Sword > Build Main Menu Scene`. `MainMenu` 씬과 스토리·머티리얼 에셋을 만들고 빌드 순서를 `MainMenu, RhythmPrototype, …`으로 맞춘다.

## 확인

- EditMode `FinaleTests`: 무력화·결정 노트·실패·이전 규칙.
- PlayMode `PrototypeIntegrationTests.ReadyCardThenPauseAndGameOverScreens`: Esc 일시정지 → 설정 열고 Esc로 닫기 → S로 고르기 → 판으로 복귀(되감기) → 시간 초과 → FALLEN → 첫 수부터.
- PlayMode `GameFlowTests`: 피그마 위치·호버·흰 버전 / 시작 → 스토리(Space·Esc) → 전투 자동 카운트인 / 자유 이동(대각선 정규화) → 잡음에 닿기 전에는 머무름 → 닿으면 컷신 → Space로 건너뛰기 → 흰 메인화면 복귀.
- 화면 캡처(필요할 때만): `-testFilter StringAsASword.Tests.MenuCaptureTests -menuCaptureDir <폴더>` → 메뉴(어두운/호버/흰/설정), 전투 화면(키 가이드, Pause, Game Over와 호버), 체스판 균열 1~3단계, 가운데가 갈라지는 과정과 터짐, 엔딩 단계별 PNG. 배치 모드에서는 `WaitForEndOfFrame`이 오지 않으므로 캔버스를 카메라로 직접 렌더한다.
