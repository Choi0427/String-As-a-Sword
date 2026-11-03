# 아트 에셋 연동 (ArtSet)

아트 에셋은 코드를 고치지 않고 `Assets/_Project/Art/ArtSet.asset`의 슬롯에 넣어서 적용한다. 비어 있는 슬롯은 기존 프로토타입 비주얼(절차적 메시, 글리프)을 그대로 쓴다. 그래서 에셋이 하나씩 들어와도 게임은 항상 실행된다. 아티스트에게 줄 사양은 Notion "아트 에셋 사양" 페이지에 있다.

## 에셋 넣는 순서

1. 받은 파일을 폴더에 넣는다. 폴더가 없으면 `String As a Sword > Art > Create Art Set and Folders`를 실행하면 만들어진다.
   - 3D: `Art/Models/{Knight,King,Board}`
   - UI 이미지: `Art/UI/{Telegraph,HUD,Menu}`
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
| `kingPrefab` | `kingAnchor` 위치에 `kingScale`로 배치. 비어 있으면 킹을 만들지 않는다 | Trigger `Telegraph` `Attack` `Hurt` `PhaseUp` (`Stunned`는 피날레용으로 예약) |
| `noisePrefab` | 잡음 대체. `noiseDormantMaterial`이 있으면 목표 달성 후 머티리얼을 그것으로 바꾼다 | Bool `Dormant` |
| `lightTile` / `darkTile` / `boardEdge` | 칸 윗면(칸마다 UV 0~1) / 받침·옆면 | — |
| `eitherMark` / `guardMark` / `dodgeMark` / `impactMark` | 예고 칸 위의 사각 판 (0.92칸) | — |
| `font` | 모든 HUD 텍스트 (한글 TMP SDF) | — |
| `background` / `noteSprite` | 전체 배경 / 좌우 노트 | — |
| `crownFull` / `crownLocked` / `crownBroken` | 보스 HP 칸 아이콘. 세 개를 모두 넣어야 표시된다 | — |
| `*Vfx` | 해당 순간에 보드 위에 생성하고 2초 뒤 삭제 | — |
| `mainMenuDark` / `mainMenuWhite` | 예약. 메인화면을 구현할 때 연결한다 | — |

- 애니메이터에 해당 파라미터가 없으면 그 신호는 조용히 건너뛴다. 경고도 나지 않는다.
- 공격·방어 연출 시간은 각각 0.23초, 0.34초다. 클립이 이보다 길면 다음 박자와 겹친다.
- `bossFireVfx`는 공격이 발동한 **모든 칸**에 생성된다. 전체 칸 공격이면 64개이므로 가볍게 만든다.

## 예고 마크 만드는 법

1. `Art/Materials/ArtCutout.mat`을 복제한다 (URP Unlit, 알파 클리핑 0.5).
2. 복제한 머티리얼의 Base Map에 PNG를 넣는다.
3. 그 머티리얼을 슬롯에 넣는다.

- 머티리얼로 받는 이유가 있다. 빌드할 때 Unity는 실제로 참조된 셰이더 변형만 남기는데, 런타임에 투명 설정을 켜면 빌드에서 그 변형이 빠질 수 있다.
- 이미지의 아래쪽 띠에는 남은 노트 숫자 배지가 겹친다. 핵심 모양은 위쪽 2/3 안에 둔다.

## 킹 배치와 카메라

- 기본 카메라(크기 4.6)에서는 8행 바로 뒤까지만 보인다. 킹을 세우면 `cameraSize`와 `cameraLift`로 화면 틀을 넓혀야 한다.
- 아래 그림이 실제로 확인한 설정이다(가로 7·높이 4 블록, 앵커 z = 4.8, 크기 5, 리프트 1.25).
- 틀을 넓히면 보드가 약 92%로 작아진다.
- 화면 고정 문구 `ATTACK ZONE / ROW 8`이 킹과 겹치므로, 킹 아트가 들어오면 HUD 배치를 다시 잡는다.

![그레이박스 스케일 확인](ArtPipeline/greybox-board.png)

`Art/Templates/`의 `Knight_Template`(0.6×1.2), `King_Template`(7×4×1), `Noise_Template`(0.6)은 바닥 중앙 피벗의 크기 기준 프리팹이다. 아티스트에게 크기 기준으로 주거나, 슬롯에 넣어 배치를 먼저 확인할 수 있다.

## 셰이더와 조명

- 권장은 URP Unlit이다. 음영은 텍스처에 그려 넣는다. 보드에는 기본적으로 라이트가 없다.
- URP Lit 머티리얼을 쓰면 ArtSet의 `useLighting`을 켠다. 보드에 Directional Light가 1개 추가된다.

## 검증

- `String As a Sword > Prepare Windows Build`를 실행하면 빈 슬롯 목록이 콘솔에 정보 로그로 나온다. 빌드 실패로 처리하지 않는다.
- PlayMode 테스트 `ArtSetIntegrationTests`는 ArtSet을 메모리에서만 그레이박스로 채운다. 프리팹이 대체되는지, 신호 경로에서 로그가 나지 않는지 확인하며 디스크에는 저장하지 않는다.
- 어두운 아트로 바꾼 뒤에는 Windows 빌드의 `--verify-prototype` 밝기 검사(밝은 픽셀 5% 초과)를 다시 확인한다.
- 전투 로직은 바뀌지 않았으므로 combatVersion은 battle-4 그대로다.
