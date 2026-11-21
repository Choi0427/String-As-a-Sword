# 인트로 에셋 (프롤로그 배경·작곡 큐·체스 클록)

인트로 리뷰 결정 중 새 에셋이 필요한 세 가지를 받는 자리다. 작곡가·아티스트용 요청서는 Notion [인트로 에셋 사양 v1](https://app.notion.com/p/41fb56a45803813f9112d1a7320d9e20)에 있다(아트 사양 v1 옆).

- **A5-B 온전한 판 배경:** 줄을 끊기 전까지 깔리는 배경 6장. 끊는 박에 앞 레이어부터 사라지며 같은 번호의 전투 배경이 드러난다.
- **S1-B 프롤로그 작곡 큐:** 프리비즈 합성음 대신 원곡 키(F#m)·템포(87 BPM)의 작곡 큐 3조각.
- **S2-A 체스 클록:** 합성 "clock" 대신 대국 시계 녹음 2테이크. 프롤로그와 튜토리얼에서 매 박 쓰인다.

파일을 넣고 메뉴를 한 번 돌리면 적용된다. 코드는 고치지 않는다. 비어 있는 슬롯은 지금 임시본이 그대로 나온다.

## 파일과 슬롯

| 파일 | 폴더 | 슬롯 | 적용 메뉴 |
|---|---|---|---|
| `BG_Whole_0..5.png` (3840×2160) | `Art/UI/Background/` | `ArtSet.prologueBackgroundLayers` (전투 `backgroundLayers`와 같은 순서·흔들림) | String As a Sword > Art > Set Up Chessboard and Background |
| `Cue_A.wav` (32박 22.069초+) | `Audio/Prologue/Cue/` | `Prologue.asset` `cueA` | String As a Sword > Audio > Set Up Prologue Audio |
| `Cue_Wait.wav` (4박 2.7586초+) | 〃 | `cueWait` | 〃 |
| `Cue_B.wav` (20박 13.793초+) | 〃 | `cueB` | 〃 |
| `Clock_Tick.wav`, `Clock_Accent.wav` | `Audio/Prologue/Clock/` | `clockTick`, `clockAccent` | 〃 |

- 소리 메뉴는 가져오기 설정을 맞춘다. 조각은 스테레오·Vorbis, 클록은 모노·PCM이며, 둘 다 노멀라이즈 끔·Decompress On Load·미리 읽기다. 조각이 박 수보다 짧으면(10ms 넘게) 경고한다.
- 작곡 큐는 `cueVolume`(0.03, 곡과 같은 배율) × 음악 볼륨으로 재생된다. 조각이 들어오면 그 구간의 음악 임시 큐(king_*, choir_*, lane_*, gliss, tune, strain_bar, stroke_E5, guitar_hold, ring5)는 빠지고 효과음은 남는다.
- 조각은 전용 AudioSource로 재생한다. 대기 마디는 소스 2개가 번갈아 마디마다 예약되어 꼬리가 다음 마디에 잘리지 않는다.

## 재생 규칙 (게임 녹음으로 확인)

- **첫 샘플 = 박.** 모든 클립은 첫 샘플이 박에 오도록 `PlayScheduled`로 예약된다.
- **줄을 끊을 때:** 대기 마디(와 Cue_A)는 끊는 소리가 시작되고 30ms 뒤에 멈춘다. 박 직후(0.25박 이내)에 늦게 누르면 그 박에 끊긴 것으로 치고, 끊는 소리는 누른 순간에 나며, Cue_B는 늦은 만큼 앞을 건너뛰고 재생해 끝을 곡 시작 박에 맞춘다.
- **곡 시작 박(이양):** 프롤로그 소리는 자르지 않고 울리게 둔다(Cue_B와 임시 소리의 꼬리 모두). 원곡 mp3는 이 박보다 0.2276초 먼저 들어오지만 처음 약 1초가 거의 무음이고 약 2.3초에 걸쳐 커지므로, 꼬리가 그 사이를 메운다.
- **튜토리얼로 넘어갈 때(처음 한 번):** 튜토리얼 클록의 첫 강세는 곡 시작 박에 온다. 튜토리얼은 그 박 다음 프레임에야 시작해 첫 박을 예약할 수 없으므로, 프롤로그가 미리 예약해 둔다.
- **ESC:** 모든 소리를 즉시 멈춘다.

## 확인 도구

- **String As a Sword > Audio > Render Intro Guide Tracks:** 게임과 같은 규칙(같은 큐·음량·자르기, 조각이 있으면 조각)으로 `output/intro-assets/`에 렌더한다.
  - `guide_A/Wait/B.wav`: 작곡가용 가이드 트랙. guide_B에는 원곡 도입 10초가 들어 있다.
  - `seam_*.wav`: 대기 마디 3회 반복, 네 박 각각에서 끊기, 곡과 튜토리얼로 넘어가기.
  - `events.csv`: 소리마다 박과 초.
  - `audio_check.txt`: 음량(원곡 대비), 클립 앞 무음, 잘리는 곳마다 얼마나 가려지는지, 이음새를 넘어 울리는 소리, 빈 박.
- **게임 녹음:** PlayMode의 `PrologueAudioTests.RecordThePrologueIntoTheSong` / `...IntoTheTutorial`(Explicit, 약 45초·20초)이 AudioListener에서 실제 출력을 녹음해 `capture_into_*.wav`와 `.json`을 쓴다. **String As a Sword > Audio > Compare Prologue Recordings**가 같은 진행의 모델과 비교해 `capture_into_*_check.txt`와 100ms 단위 `_profile.csv`를 쓴다. 2026-11-22 기준 두 녹음 모두 차이가 구간 A·대기·B에서 −21~−30 dB, 곡 진입 뒤 −53 dB이고 모든 소리가 제 음량(±3 dB)으로 있다. 남는 차이는 클록 어택의 첫 몇 ms뿐이다.
- **듣기:** `output/intro-assets/listen.py`가 Earshot의 음성(Windows, 한국어)으로 이름을 읽고 파일을 끝까지 튼다. `python listen.py game|seams|guides`.
- 모델의 음량은 출력의 모노 합 기준이다. Unity는 2D 소스의 모노 클립을 양쪽 채널에 3 dB 낮춰 내보내고 스테레오 클립은 그대로 낸다(녹음과 맞춰 확인).
