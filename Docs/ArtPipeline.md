# 아트 에셋 연동 (ArtSet)

아트 에셋은 코드를 고치지 않고 `Assets/_Project/Art/ArtSet.asset`의 슬롯에 넣어서 적용한다. 비어 있는 슬롯은 기존 프로토타입 비주얼(절차적 메시, 글리프)을 그대로 쓴다. 그래서 에셋이 하나씩 들어와도 게임은 항상 실행된다. 아티스트에게 줄 사양은 Notion "아트 에셋 사양" 페이지에 있다.

## 에셋 넣는 순서

1. 받은 파일을 폴더에 넣는다. 폴더가 없으면 `String As a Sword > Art > Create Art Set and Folders`를 실행하면 만들어진다.
   - 3D: `Art/Models/{Knight,King,Board}`
   - UI 이미지: `Art/UI/{Telegraph,HUD,Menu,Background}`
   - VFX 프리팹: `Art/VFX`
   - 폰트: `Art/Fonts`
2. 첫 임포트 때 `ArtImportRules`가 기본 설정을 자동으로 적용한다. 그 뒤 Inspector에서 바꾼 값은 유지된다.
   - Models: 스케일 1(파일 단위 무시), 카메라·라이트 제외, Generic 리그
   - UI: Sprite, 밉맵 끔, Clamp
3. FBX로 프리팹을 만들고 Animator Controller를 연결한다. 트리거 이름은 아래 표와 정확히 같아야 한다.
4. `ArtSet.asset`을 선택하고 슬롯에 끌어 넣는다.
5. Play를 다시 시작하면 적용된다. 설정은 시작할 때 한 번만 읽는다.

## 슬롯과 연결

| 슬롯 | 쓰이는 곳 | 애니메이터 파라미터 |
|---|---|---|
| `knightPrefab` | 나이트 대체. 이동 트윈·점프는 코드가 루트에서 계속 처리한다 | Trigger `Move` `Attack` `Guard` `Hit` `Block` |
| `kingPrefab` | `kingAnchor` 위치에 `kingScale`로 배치. 비어 있으면 킹 모델을 만들지 않고 `bossImage`를 쓴다 | Trigger `Telegraph` `Attack` `Hurt` `PhaseUp` (`Stunned`는 피날레용으로 예약) |
| `bossImage` | 임시 보스 그림(2D). 보드 뒤, 배경 위에 그린다. `bossImageHeight`(캔버스 px)로 크기를, `bossImageSink`(x 좌우, y 보드 먼 변 아래로 내리는 양)로 위치를 맞춘다. 피격 시 흔들리고, 스턴되면 어두워지며 기울고, 피날레에서 가라앉는다. `kingPrefab`이 있으면 쓰지 않는다 | — |
| `noisePrefab` | 잡음 대체. `noiseDormantMaterial`이 있으면 목표 달성 후 머티리얼을 그것으로 바꾼다 | Bool `Dormant` |
| `noiseSprite` | `noisePrefab`이 비어 있을 때 플레이어가 모으는 잡음을 이 그림(`Art/UI/Noise/Noise.png`)으로 그린다. 칸 1.7배 너비로 칸 위 0.5칸 높이에 떠서 카메라를 보며 위아래로 흔들리고, 칸에는 부드러운 그림자가 진다(내려올수록 작고 진해짐). 페이즈 목표를 채우면 회색으로 흐려진다 | String As a Sword > Art > Set Up Noise Sprite로 연결한다 |
| `boardPrefab` | 절차적 받침·테두리·덱·64칸을 통째로 대체. 8×8 칸이 1유닛 칸에 맞고 윗면이 y = 0이어야 한다. 싸비2가 끝난 뒤부터 칸 단위 64조각으로 나뉘어 몇 칸이 내려앉고, 곡이 끝나면 가운데 4×4 조각이 튕겨 나간다. 모델의 Read/Write가 켜져 있어야 하며(`ChessBoard.fbx`는 켜 둠), 꺼져 있으면 금만 가고 조각나지 않는다 | — |
| `boardCrack` | 보드 전체에 덮는 금·가운데 빛·구멍 머티리얼(`Board Crack` 셰이더의 `_Crack`, `_Light`, `_Hole`을 코드가 움직인다). `String As a Sword > Art > Set Up Board Cracks`가 만든다 | — |
| `endingCutscene` | 엔딩 컷신 영상(`VideoClip`). 잡음에 닿으면 레터박스 사이에서 재생된다. 비어 있으면 검정 화면 2초 | — |
| `lightTile` / `darkTile` / `boardEdge` | 칸 윗면(칸마다 UV 0~1) / 받침·옆면 | — |
| `normalMark` / `strongMark` | 공격 장판 (0.92칸). 그림 없이 `Telegraph Tile` 셰이더가 그린다. 일반은 반투명 흰색에 테두리 안쪽 글로우, 강화(가드 불가)는 더 투명하고 대각선 빗금이 있다. 보드 전체 공격(가드 필수)은 일반 장판으로 보인다 | `String As a Sword > Art > Set Up Telegraph Tiles`가 `TelegraphNormal.mat` / `TelegraphStrong.mat`을 만들어 넣는다. 투명도·글로우·빗금 값은 머티리얼에서 고친다 |
| `impactMark` | 공격이 떨어진 칸에 0.3초 보이는 표시. 비어 있으면 X 모양 메시를 쓴다 | — |
| `font` | 모든 HUD 텍스트 (한글 TMP SDF) | — |
| `background` | 전체 배경 | — |
| (판정선 글로우) | `laneSprite`가 있으면 흰 판정선 뒤에 흰색 글로우를 코드로 그린다. 박자마다 글로우가 밝아진다 | — |
| `laneSprite` | 좌우 현과 프렛을 한 장에 그린 판정선 그림. 가운데 빈 곳에 보드가 들어간다. 절차적 현을 대체한다. 높이는 `laneRows`의 보드 행(기본 3~6행)에, 너비는 가운데 현이 보드 옆 가장자리에서 끝나도록 따로 맞춘다. 판정선은 같은 행 범위에서 보드 가장자리를 따라 기울여 그리고, 노트 기둥도 같은 각도로 기운다 | `laneRows`(현이 걸치는 가장 가까운 행·먼 행), `laneNoteString`(노트가 타는 현의 y 픽셀. 기본 190은 넷째 현으로 4행과 3행의 경계다), `laneBoardGap`(가운데 현이 끊기는 좌·우 x 픽셀), `laneStrings`(맨 위·가운데·맨 아래 현의 y 픽셀), `laneBoardEdge`(보드 옆 가장자리의 보드 좌표 x)를 그림에 맞게 넣는다 |
| `noteSprite` | 좌우 노트. 기둥이 위, 머리가 아래인 그림 한 장으로 네 종류를 그린다. 기본 노트는 그대로, 빠른 노트 2개 묶음은 빔 1줄, 3개 묶음은 빔 2줄로 잇고, 홀드 노트는 꼬리까지 두 줄 레일을 긋는다 | 간격이 0.75박보다 짧은 노트를 한 묶음으로 본다. 4개 이상은 3개와 2개로 나눈다. 빔과 레일은 코드가 그린다 |
| `backgroundLayers` | `background` 위에 뒤→앞 순서로 쌓는 전체 화면 레이어. `sway`(캔버스 px)가 0이 아니면 `period`초 주기로 둥둥 떠다닌다. 배경이 있으면 보드 영역이 투명해져 배경 위에 보드가 놓인다 | — |
| `holdBodySprite` / `holdTailSprite` | 홀드 노트의 현(줄) 몸통 / 꼬리 말뚝 | 흰색으로 그린다. 가로로 늘어나며, 상태별 색(대기 흰색·유지 중 민트·끊김 회색)을 코드가 입힌다 |
| `crown` / `crownRegions` / `crownMaterial` | 보스 그림 머리 위의 왕관(`Art/UI/Boss/Crown.png`). 보스 HP와 잡음 표시를 대신한다. 날 4개가 HP 4칸이다. 오른쪽 날부터 맞을 때마다 위에서 아래로 나이트 검정이 되고, 칸이 다 닳으면 그 날의 glow가 꺼진다. 날 사이 이음새 3개는 잡음 페이즈 1~3이다. 오른쪽 이음새부터 잡음이 날아와 닿을 때마다 오른쪽에서 검게 물들고, 요건을 채우면 지지직 노이즈가 붙는다. 보스가 쓰러지면 왕관 전체가 꺼진다. 세 슬롯이 모두 있어야 표시된다 | 그림을 바꾸면 String As a Sword > Art > Set Up Boss Crown을 다시 실행한다. 영역 맵(`Crown_Regions.png`)과 `Crown.mat`을 다시 굽고, 영역 확인 그림을 `Logs/CrownRegions.png`에 쓴다. 날·이음새의 위치는 `CrownLayout.cs`의 UV 값이다. `crownWidth`(보스 그림 너비 대비), `crownSeamHeight`(가운데 이음새가 놓이는 보스 그림 높이, 위=0)로 크기와 위치를 맞춘다 |
| `*Vfx` | 해당 순간에 보드 위에 생성하고 2초 뒤 삭제 | — |
| `mainMenuDark` / `mainMenuWhite` | 예약. 메인화면을 구현할 때 연결한다 | — |
| `menuFont` | 메인화면·스토리 카드·컷신 건너뛰기 안내 (학교안심 곧은제목 B, 동적 TMP SDF) | — |

- 애니메이터에 해당 파라미터가 없으면 그 신호는 조용히 건너뛴다. 경고도 나지 않는다.
- 공격·방어 연출 시간은 각각 0.23초, 0.34초다. 클립이 이보다 길면 다음 박자와 겹친다.
- `bossFireVfx`는 공격이 발동한 **모든 칸**에 생성된다. 전체 칸 공격이면 64개이므로 가볍게 만든다.

## 공격 장판과 착탄 표시

- 공격 장판 2종은 `Art/Shaders/TelegraphTile.shader`가 칸의 UV로 그린다. 텍스처가 없다. `Set Up Telegraph Tiles` 메뉴를 다시 실행하면 두 머티리얼이 기본값으로 돌아간다.
- 보드는 렌더 텍스처에 그려 UI에 올린다. 그래서 이 셰이더는 알파 채널을 `One OneMinusSrcAlpha`로 섞는다. 일반 알파 블렌드를 쓰면 장판 자리의 보드가 배경으로 비쳐 보인다.
- 착탄 표시(`impactMark`)를 그림으로 바꾸려면 `Art/Materials/ArtCutout.mat`(URP Unlit, 알파 클리핑 0.5)을 복제해 Base Map에 PNG를 넣고 슬롯에 넣는다.
- 머티리얼로 받는 이유가 있다. 빌드할 때 Unity는 실제로 참조된 셰이더 변형만 남기는데, 런타임에 투명 설정을 켜면 빌드에서 그 변형이 빠질 수 있다.
- 이미지의 아래쪽 띠에는 남은 노트 숫자 배지가 겹친다. 핵심 모양은 위쪽 2/3 안에 둔다.

## 킹 배치와 카메라

- `cameraElevation`(0 = 스테이지 값 60°)과 `cameraFieldOfView`(0 = 직교)로 보드를 내려다보는 각도와 원근을 바꾼다. 예고 마크는 칸 위에 누운 3D 판이라 시점을 따라 눕고, 숫자 배지와 A–H·1–8 라벨은 투영 위치를 따라간다.
- 기본 카메라(크기 4.6)에서는 8행 바로 뒤까지만 보인다. 킹을 세우면 `cameraSize`와 `cameraLift`로 화면 틀을 넓혀야 한다.
- 아래 그림이 실제로 확인한 설정이다(가로 7·높이 4 블록, 앵커 z = 4.8, 크기 5, 리프트 1.25).
- 틀을 넓히면 보드가 약 92%로 작아진다.
- 화면 고정 문구 `ATTACK ZONE / ROW 8`은 레퍼런스 화면 정리 때 없앴다. 8행은 금색 공격선으로만 표시한다. 킹을 세우면 상단의 보스 HP 칸과 겹치지 않는지 확인한다.

![그레이박스 스케일 확인](ArtPipeline/greybox-board.png)

`Art/Templates/`의 `Knight_Template`(0.6×1.2), `King_Template`(7×4×1), `Noise_Template`(0.6)은 바닥 중앙 피벗의 크기 기준 프리팹이다. 아티스트에게 크기 기준으로 주거나, 슬롯에 넣어 배치를 먼저 확인할 수 있다.

## 셰이더와 조명

- 권장은 URP Unlit이다. 음영은 텍스처에 그려 넣는다. 보드에는 기본적으로 라이트가 없다.
- URP Lit 머티리얼을 쓰면 ArtSet의 `useLighting`을 켠다. 보드에 Directional Light가 1개 추가된다.

## 검증

- `String As a Sword > Prepare Windows Build`를 실행하면 빈 슬롯 목록이 콘솔에 정보 로그로 나온다. 빌드 실패로 처리하지 않는다.
- PlayMode 테스트 `ArtSetIntegrationTests`는 ArtSet을 메모리에서만 그레이박스로 채운다. 프리팹이 대체되는지, 신호 경로에서 로그가 나지 않는지 확인하며 디스크에는 저장하지 않는다.
- 어두운 아트로 바꾼 뒤에는 Windows 빌드의 `--verify-prototype` 밝기 검사(밝은 픽셀 5% 초과)를 다시 확인한다.
- 전투 로직은 바뀌지 않았으므로 combatVersion은 바뀌지 않았다(당시 battle-4).

## 체스판 모델과 게임 배경 (2026-11-10 전달분)

- `String As a Sword > Art > Set Up Chessboard and Background`를 실행하면 아래 설정이 한 번에 적용된다. 다시 실행하면 원본 파일에서 머티리얼과 프리팹을 새로 만든다.
  - 보드: `Art/Models/Board/ChessBoard.fbx`(Meshy)로 `ChessBoard_Lit.mat`(URP Lit, 베이스컬러 + 노멀, 메탈릭 0, 스무스니스 0.2)과 `ChessBoard.prefab`을 만든다. `useLighting`을 켠다.
  - 보드 맞춤: 모델은 정사각형이 아니다(가로 9.04 : 세로 8.21). 그래서 칸 영역 비율을 축마다 따로 잰 값(`BoardArtSetup.PlayShare`, x 0.913 / z 0.894)으로 `Fit` 노드에 스케일을 준다. 위에서 찍은 렌더에서 칸 경계가 1유닛 격자와 맞는 것을 확인했다. 모델을 바꾸면 이 값을 다시 잰다.
  - 카메라: 고도 35°, 원근 FOV 30°, 크기 5.1.
  - 배경: `Art/UI/Background/BG_0..5.png`를 뒤→앞 0, 1, 4, 5, 2, 3 순서로 쌓는다. 1·4·5는 떠다닌다.
- 메탈릭·러프니스 텍스처는 돌 재질이라 쓰지 않아서 프로젝트에 넣지 않았다.

## 나이트 모델 (2026-11-14 전달분)

- `String As a Sword > Art > Set Up Knight Model`을 실행하면 아래 설정이 한 번에 적용된다. 다시 실행하면 원본 파일에서 머티리얼과 프리팹을 새로 만든다.
  - `Art/Models/Knight/Knight.fbx`와 `Knight_BaseColor.jpg`로 `Knight_Lit.mat`(URP Lit, 베이스컬러만, 메탈릭 0, 스무스니스 0.2)과 `Knight.prefab`을 만들어 `knightPrefab` 슬롯에 넣는다.
  - 맞춤: 원본은 0.39 × 1.00 × 0.36 크기다. `Fit` 노드에서 높이 1.2유닛(`KnightArtSetup.Height`)으로 균일하게 키우고, 발밑 중앙을 원점에 둔다. 방향은 `KnightArtSetup.Yaw`로 돌리며, 원본이 이미 8행(보스) 쪽을 봐서 0이다.
- 모델에는 리그와 애니메이션이 없어서 Animator를 붙이지 않았다. 이동·점프·스쿼시와 칼 궤적·방패 라인은 코드 연출 그대로다.
- 같은 날 시작 칸을 d4에서 나이트의 실제 시작 칸인 g1로 바꿨다(`BattleSession`). 채보 밸런스는 d4 기준으로 맞춘 것이라 다시 확인해야 한다.
