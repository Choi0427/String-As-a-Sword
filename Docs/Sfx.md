# 효과음 (UI)

게임 효과음은 UI 두 개와 엔딩 붐 하나다. UI 소리는 프롤로그에서 쓰는 소리를 그대로 쓴다. 판정·공격·잡음에는 효과음이 없다(곡이 타격감을 낸다).

| 슬롯 (`Art/ArtSet.asset`) | 파일 | 음량 | 언제 |
|---|---|---|---|
| `uiMoveSound` | `Audio/Prologue/clock.wav` (프롤로그 메트로놈) | `uiMoveVolume` 0.036 (프롤로그의 보통 박: 0.12 × 0.3) | 메인 메뉴·일시정지·게임오버·설정에서 다른 항목을 고를 때(W/S, 마우스 올림), 설정 A/D 한 번(누르고 있을 때 반복은 무음) |
| `endingSound` | `Audio/Ending/ending_boom.wav` (시네마틱 붐, 5.6초로 자르고 3.4초부터 페이드아웃) | `endingSoundVolume` 0.11 (가장 가까울 때의 엔딩 잡음보다 조금 크게) | 엔딩에서 易聲 그림이 나타날 때 한 번 |
| `uiConfirmSound` | `Audio/Prologue/place.wav` (프롤로그 체스말 이동) | `uiConfirmVolume` 0.12 (프롤로그와 같음) | 항목을 누를 때(Enter, Space, 클릭), 난이도 선택, 튜토리얼 버튼 |

- `Sfx.UiMove` / `Sfx.UiConfirm`이 씬 전환에도 살아 있는 2D 소스 8개로 돌려 가며 재생한다. 메뉴의 확인음은 전투 씬으로 넘어가도 끝까지 울린다.
- 음량은 위 값 × 설정의 **음량**(`GameFlow.MusicVolume`)이다. 크기는 ArtSet 인스펙터에서 바꾼다.
- 슬롯이 비면 무음이다.
