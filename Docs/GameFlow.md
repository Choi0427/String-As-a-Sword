# 전체 흐름: 메인화면 → 스토리 → 전투 → 엔딩 → 흰 메인화면

2026-11-08. 전투 설정 battle-6 (보스 HP 35/8/14/54, 측정은 [ChartBalance.md](ChartBalance.md)의 battle-6 절).

## 흐름

| 단계 | 씬 | 담당 | 내용 |
| --- | --- | --- | --- |
| 메인화면 | `MainMenu` | `MainMenuView` | 피그마 `Main Menu`(node 2:3) 그대로. 시작 / 설정 / 종료 |
| 스토리 | `MainMenu` | `MainMenuView` | 자막 카드. Space·Enter·클릭 다음, Esc 전부 건너뛰기. 시작할 때마다 나온다 |
| 전투 | `RhythmPrototype` | `PrototypeGame` | 메뉴에서 오면 준비 화면 없이 바로 카운트인 |
| 무력화 | 〃 | `BattleSession` + `FinaleDirector` | 보스 HP를 다 깎으면 킹이 흔들리고 공격·예고가 멈춘다. 체스판 바깥 칸이 떨어지기 시작 |
| 격파 | 〃 | 〃 | 곡 끝 16분 3연노트(304~306번)가 시작될 때 승리 확정. 3연타마다 섬광, 이후 킹이 쓰러짐 |
| 마무리 | 〃 | 〃 | 남은 14노트는 계속 흐르지만 판정·Miss·페널티 없음 |
| 자유 이동 | 〃 | `FinaleDirector` | 곡이 끝나면 HUD와 남은 판이 사라지고, 허공에서 WASD·방향키로 6초간 격자 없이 이동(대각선도 같은 속도). 주변이 점점 밝아진다 |
| 엔딩 | 〃 | 〃 | 화면이 흰색으로 차오르고 엔딩 문구 3줄 → 클리어 저장 → 메인화면 |
| 흰 메인화면 | `MainMenu` | `MainMenuView` | 체스판·나이트는 그대로, 왼쪽(여백·로고·메뉴)만 반전되어 흰색 |

3연노트까지 HP를 다 깎지 못하면 실패(시간 초과)다. 전투 중 일시정지 메뉴의 버튼은 메인화면으로 돌아간다.

## 규칙 (battle-6)

- `SongDefinition.finishNote`(Inspector): 마무리 연타의 첫 노트. 현재 304. -1이면 이전 규칙(HP 0 즉시 승리, 마지막 노트 후 시간 초과).
- `BattleSettings.FinishNote` / `DecidingNote`(= FinishNote - 1): 이 노트가 닫힐 때 무력화 상태면 승리, 아니면 실패.
- 무력화 순간 `BossPatternSystem.Halt()`가 공개된 예고를 취소하고 이후 공격을 내지 않는다. 로그 이벤트 `king-stunned`.
- 봇 측정의 "남은 노트"는 이제 무력화부터 결정 노트까지의 여유다.

## 메인화면

- 배경: `Art/UI/Menu/MainMenu_Logo_BG.png`(피그마에서 받은 3840×2160) → `ArtSet.mainMenuDark`.
- 글자: 80px, `#D9D9D9`, 자간 -3.2px, 왼쪽 184.5px / 위 631·733·835px. 호버: `#929292` 상자 + `#0C0C0C` 글자. 하단 "Inspired by 이승윤 <검을 현>" 16px `#929292`.
- 폰트: 학교안심 고든제목 B를 `Art/Fonts/`에 넣고 TMP 폰트 에셋으로 만든 뒤 `ArtSet.menuFont`에 지정한다. 비어 있으면 실행 중 OS 한글 폰트(맑은 고딕)를 동적으로 쓴다(파일을 프로젝트에 복사하지 않음). 피그마와 글자 모양·높이가 달라 호버 상자와 어긋나는 것은 폰트가 들어오면 맞춰진다.
- 흰 버전: `Art/Shaders/MenuInvertLeft.shader`가 이미지 왼쪽만 색을 반전한다. 경계는 아래 36% → 위 45%로 기울고 3.5% 번진다(머티리얼 `Art/Materials/MenuInvertLeft.mat`에서 조정). `ArtSet.mainMenuWhite`에 손으로 만든 흰 이미지를 넣으면 그것을 쓴다.
- 클리어 기록은 저장되어 다음 실행에도 흰색이다. 메인화면에서 **Shift+R**로 되돌린다.
- 설정: 음악 볼륨(0~100%), 입력 싱크·화면 싱크(±200ms). 저장되며 곡 에셋 값 위에 더해진다.
- 키보드: W/S·방향키로 고르고 Enter·Space로 누른다.

## 스토리 문구 수정

`Data/Story/IntroStory.asset`의 카드 목록. 카드마다 문장과 선택 그림(`image`). 현재 문구는 GDD 콘셉트(잡음·주선율·흑백 체스판)로 쓴 초안이다. 엔딩 문구는 `FinaleDirector.EndingLines`.

## 씬 다시 만들기

`String As a Sword > Build Main Menu Scene`. `MainMenu` 씬과 스토리·머티리얼 에셋을 만들고 빌드 순서를 `MainMenu, RhythmPrototype, …`으로 맞춘다.

## 확인

- EditMode `FinaleTests`: 무력화·결정 노트·실패·이전 규칙.
- PlayMode `GameFlowTests`: 피그마 위치·호버·흰 버전 / 시작 → 스토리(Space·Esc) → 전투 자동 카운트인 / 자유 이동(대각선 정규화) → 흰 메인화면 복귀.
- 화면 캡처(필요할 때만): `-testFilter StringAsASword.Tests.MenuCaptureTests -menuCaptureDir <폴더>` → 메뉴(어두운/호버/흰/설정)와 엔딩 단계별 PNG. 배치 모드에서는 `WaitForEndOfFrame`이 오지 않으므로 캔버스를 카메라로 직접 렌더한다.
