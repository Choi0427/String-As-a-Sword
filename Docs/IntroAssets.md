# 인트로 에셋 (프롤로그 배경·작곡 큐·체스 클록·줄 녹음)

인트로 리뷰 결정과 프롤로그 개편(R6-A) 중 새 에셋이 필요한 네 가지를 받는 자리다. 작곡가·아티스트용 요청서는 Notion [인트로 에셋 사양 v1](https://app.notion.com/p/41fb56a45803813f9112d1a7320d9e20)에 있다(아트 사양 v1 옆).

- **A5-B 온전한 판 배경:** J로 현을 뜯기 전까지 깔리는 배경 6장. 뜯는 박에 앞 레이어부터 사라지며 같은 번호의 전투 배경이 드러난다.
- **S1-B 프롤로그 작곡 큐:** 원곡 키(F#m)·템포(87 BPM)의 작곡 큐 3조각. 프롤로그의 소리 5종 아래에 깔린다(개편 후 임시 음악은 없다).
- **S2-A 체스 클록:** 합성 "clock" 대신 대국 시계 녹음 2테이크. 프롤로그와 튜토리얼에서 매 박 쓰인다.
- **R6-A 줄 녹음:** 팽팽한 줄이 버티는 소리(`String_Strain`)와 현을 세게 뜯어 떨어져 나가는 소리(`String_Pluck`, J "현을 뜯는다") 실제 녹음 2개. 뜯는 소리는 끊어지는 소리가 아니라 현을 퉁기는 어택이 먼저 들려야 한다. 없는 동안 버티는 소리는 나지 않고, 뜯는 소리는 지직(잡음)과 무겁게 놓는 말 소리가 대신한다.

파일을 넣고 메뉴를 한 번 돌리면 적용된다. 코드는 고치지 않는다. 비어 있는 슬롯은 지금 임시본이 그대로 나온다.

## 파일과 슬롯

| 파일 | 폴더 | 슬롯 | 적용 메뉴 |
|---|---|---|---|
| `BG_Whole_0..5.png` (3840×2160) | `Art/UI/Background/` | `ArtSet.prologueBackgroundLayers` (전투 `backgroundLayers`와 같은 순서·흔들림) | String As a Sword > Art > Set Up Chessboard and Background |
| `Cue_A.wav` (32박 22.069초+) | `Audio/Prologue/Cue/` | `Prologue.asset` `cueA` | String As a Sword > Audio > Set Up Prologue Audio |
| `Cue_Wait.wav` (4박 2.7586초+) | 〃 | `cueWait` | 〃 |
| `Cue_B.wav` (20박 13.793초+) | 〃 | `cueB` | 〃 |
| `Clock_Tick.wav`, `Clock_Accent.wav` | `Audio/Prologue/Clock/` | `clockTick`, `clockAccent` | 〃 |
| `String_Strain.wav` (3초 이하), `String_Pluck.wav` (2초 이하) | `Audio/Prologue/String/` | `stringStrain`, `stringPluck` | 〃 |

- 소리 메뉴는 가져오기 설정을 맞춘다. 조각은 스테레오·Vorbis, 클록과 줄 녹음은 모노·PCM이며, 모두 노멀라이즈 끔·Decompress On Load·미리 읽기다. 조각이 박 수보다 짧으면(10ms 넘게), 줄 녹음이 길면 경고한다. 줄 녹음은 첫 샘플이 소리의 시작이어야 한다(박에 예약된다).
- 작곡 큐는 `cueVolume`(0.03, 곡과 같은 배율) × 음악 볼륨으로 재생되고, 소리 5종은 그대로 위에 난다.

## 소리 5종 (프롤로그 개편 R6-A)

합성 임시음 26종은 지웠다. 남은 소리는 다섯 가지와 침묵이다. 박은 story beat.

| 소리 | 클립 | 어디서 |
|---|---|---|
| 체스 클록 | `clock.wav` (또는 `Clock_Tick`/`Clock_Accent`) | 0~31박 매 박(끊기지 않아야 엇박이 들린다). 대기 마디 매 박. 36~39박(현이 검이 될 때)은 비어 있다. 40~55박 매 박 |
| 말 놓는 소리 | `place.wav` | 4~9박 매 박(한 박 한 수). 나이트 1회차 착지 11박, 되돌려짐 12·14박(모두 박 위). 2회차 착지 13.5박(박 사이). 13·15박은 비어 있다. 17박 나이트가 제 칸에 박힐 때 1.8배 |
| 지직 | `noise.wav` (엔딩 잡음과 같은 화이트 노이즈를 0.45초 지직거리게) | 13.5박 엇박 착지 = 잡음 탄생(`IntroDirector.NoiseVolume` 0.3), 15.5박 3회차 도약(0.42) |
| 줄 버티는 소리 | `stringStrain` | 16.2박 현이 다리를 감을 때, 22·26박 몸부림, 대기 마디 첫 박 |
| 현을 뜯는 소리 | `stringPluck` | J로 뜯는 박(36). 없으면 지직 + 1.8배 말 소리 |
- 조각은 전용 AudioSource로 재생한다. 대기 마디는 소스 2개가 번갈아 마디마다 예약되어 꼬리가 다음 마디에 잘리지 않는다.

## 재생 규칙 (게임 녹음으로 확인)

- **첫 샘플 = 박.** 모든 클립은 첫 샘플이 박에 오도록 `PlayScheduled`로 예약된다.
- **J로 현을 뜯을 때:** 대기 마디(와 Cue_A)는 뜯는 소리가 시작되고 30ms 뒤에 멈춘다. 박 직후(0.25박 이내)에 늦게 누르면 그 박에 뜯은 것으로 치고, 뜯는 소리는 누른 순간에 나며, Cue_B는 늦은 만큼 앞을 건너뛰고 재생해 끝을 이양 박에 맞춘다.
- **이양 박(구간 B 20박):** 프롤로그는 언제나 튜토리얼로 넘어간다(시작할 때마다 프롤로그 → 튜토리얼 → 곡, Esc로 각각 건너뜀). 이 박부터는 음악 없이 튜토리얼의 체스 클록만 이어진다. 프롤로그 소리는 자르지 않고 그 위로 울리게 둔다(Cue_B와 임시 소리의 꼬리 모두).
- **튜토리얼 클록의 첫 강세:** 이양 박에 온다. 튜토리얼은 그 박 다음 프레임에야 시작해 첫 박을 예약할 수 없으므로, 프롤로그가 미리 예약해 둔다.
- **곡:** 튜토리얼이 끝나면 체스 클록이 한 박씩 잦아들어 사라지고, 그 다운비트에 곡의 도입이 시작된다(카운트인 없음). 프롤로그 소리와는 만나지 않는다.
- **ESC:** 모든 소리를 즉시 멈춘다.

## 확인 도구

- **String As a Sword > Audio > Render Intro Guide Tracks:** 게임과 같은 규칙(같은 큐·음량·자르기, 조각이 있으면 조각)으로 `output/intro-assets/`에 렌더한다.
  - `guide_A/Wait/B.wav`: 작곡가용 가이드 트랙. guide_B 뒤에는 튜토리얼 클록 8박이 이어진다.
  - `seam_*.wav`: 대기 마디 3회 반복, 네 박 각각에서 끊기, 튜토리얼로 넘어가기.
  - `events.csv`: 소리마다 박과 초.
  - `audio_check.txt`: 음량(원곡 대비), 클립 앞 무음, 잘리는 곳마다 얼마나 가려지는지, 이음새를 넘어 울리는 소리, 빈 박.
- **게임 녹음:** PlayMode의 `PrologueAudioTests.RecordTheWholePrologue` / `RecordThePrologueIntoTheTutorial`(Explicit, 약 45초·20초)이 AudioListener에서 실제 출력을 녹음해 `capture_full.wav` / `capture_into_tutorial.wav`와 `.json`을 쓴다. **String As a Sword > Audio > Compare Prologue Recordings**가 같은 진행의 모델과 비교해 `*_check.txt`와 100ms 단위 `_profile.csv`를 쓴다. 2026-11-22 기준 두 녹음 모두 차이가 구간 A·대기·B에서 −21~−30 dB이고 모든 소리가 제 음량(±3 dB)으로 있다. 남는 차이는 클록 어택의 첫 몇 ms뿐이다.
- **듣기:** `output/intro-assets/listen.py`가 Earshot의 음성(Windows, 한국어)으로 이름을 읽고 파일을 끝까지 튼다. `python listen.py game|seams|guides`.
- 모델의 음량은 출력의 모노 합 기준이다. Unity는 2D 소스의 모노 클립을 양쪽 채널에 3 dB 낮춰 내보내고 스테레오 클립은 그대로 낸다(녹음과 맞춰 확인).
