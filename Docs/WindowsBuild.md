# Windows 빌드 및 실행 검증

2026년 10월 9일, Unity 6000.5.5f1 / Windows x64 / 일반 빌드(BuildOptions.None).

## 실행 파일

`C:\Users\alstj\OneDrive\바탕 화면\학교\2026-2 4-2\media project\bulid file\v1\String As a Sword.exe`

실행 후 Start 버튼 또는 Space/Enter로 시작한다. WASD 이동, J 공격, K 방어 동작, R 재시작, Esc 정지.

배포할 때는 exe와 같은 폴더의 `String As a Sword_Data`, `UnityPlayer.dll`, `MonoBleedingEdge`, D3D12 및 기타 런타임 파일을 함께 전달한다. exe만 복사하면 실행에 필요한 파일이 누락된다. `String As a Sword_BurstDebugInformation_DoNotShip`은 Unity가 생성하는 디버그 정보 폴더다.

## 멈춤 원인과 수정

기존 Player.log에서 `ArgumentNullException: shader`를 확인했다. `BoardWorldPresenter.Material()`이 Shader.Find로 이름만 찾아 머티리얼을 만들었으므로, 에디터에서 사용 가능하던 URP Unlit 셰이더가 빌드의 의존 에셋에 포함되지 않았다. 보드와 UI 초기화가 실패하면서 시작 입력 이벤트 연결도 실행되지 않았다.

- `PrototypeBoard.mat`을 생성하고 `PrototypeView`의 직렬화 필드에서 직접 참조한다.
- 런타임 색상별 머티리얼은 이 원본을 복제한다. 셰이더를 이름으로 검색하지 않는다.
- 씬 생성 도구도 같은 참조를 연결한다.
- 일반 빌드에서도 머티리얼 참조가 없으면 빌드 단계에서 실패하도록 검사한다.
- Windows 빌드 도구는 프로토타입 씬만 포함하고 BuildReport를 저장한다.
- 런타임 MCP 서버는 비활성으로 명시했다.

기존 사용자의 음량(0.03), 음원, 싱크 오프셋 및 렌더 설정을 사용해 빌드했다.

## 다시 빌드

1. 씬을 저장하고 Play 모드를 종료한다.
2. `String As a Sword > Prepare Windows Build`를 실행한다.
3. Unity Build Profiles에서 Windows 플랫폼과 `Assets/_Project/Scenes/RhythmPrototype.unity`를 선택해 빌드한다.

자동화에서는 `StringAsASword.Editor.PrototypeBuild.BuildWindows(outputDirectory)`를 에디터에서 호출할 수 있다. 배치 실행 진입점은 `BuildFromCommandLine`, 출력 인자는 `-prototypeBuildOutput <directory>`다.

## 검증 결과

- 보드·노트 렌더링 및 직렬화 셰이더 참조 PlayMode 회귀 테스트 1개 통과.
- 실제 v1 실행 파일의 진단 실행 종료 코드 0, 검증 결과 passed=true, 런타임 오류 0건.
- 시작 화면, 64칸 보드와 셰이더, Space 시작, 카운트인·비트 진행, 음악 재생, WASD 착지 수집, 최상단 J 공격·HP 하한, K 홀드 반복 방지, R 초기화, Start 버튼을 검증했다.
- 보드 RenderTexture를 실행 파일에서 PNG로 저장해 확인했다. 화면을 숨긴 진단 실행에서도 보드가 렌더링됐다.
- 최종 BuildReport: Succeeded, 오류 0건, 약 111.8MB.
- 경고 1건: Pipeline 패키지가 런타임 MCP 비활성 설정을 경고로 알린다. 게임 실행 실패를 뜻하지 않으며 일반 빌드에 MCP 서버를 켜지 않았다.

로컬 검증 자료:

- `Logs/Build/windows-build-report.json`
- `Logs/Build/Standalone/standalone-verification.json`
- `Logs/Build/Standalone/Player.log`
- `Logs/Build/Standalone/standalone-board.png`

같은 실행 파일에 `--verify-prototype --verification-output <directory>`를 전달하면 로컬 입력 이벤트를 생성해 위 흐름을 검증하고 자동 종료한다. 옵션 없이 실행하면 일반 게임이 시작된다. 진단은 지정 폴더에 결과를 기록하며, 게임 진행 확인을 위해 음원을 약 20초 재생한다.

빌드 오류 수정 커밋 메시지: `fix: retain board shader dependencies and verify Windows player builds`
