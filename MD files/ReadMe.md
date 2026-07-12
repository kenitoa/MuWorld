# MuWorld

MuWorld는 Windows용 WinForms 리듬게임입니다. 곡을 고르고, 4K/5K/6K/7K 레인으로 노트를 입력하며, 플레이 결과를 점수, 정확도, 등급, 클리어 타입, 콤보 기록으로 확인하는 게임입니다.

이 문서는 처음 실행하는 사용자를 위한 설명서입니다. 구현 구조와 내부 동작은 [Tech.md](Tech.md), 앞으로 고치거나 추가해야 할 항목은 [addMe.md](addMe.md)에 따로 정리했습니다.

## 실행 준비

필요한 환경:

- Windows
- 저장소에 포함된 `.NET 9` 로컬 SDK 또는 시스템에 설치된 .NET 9 SDK
- PowerShell 또는 명령 프롬프트

가장 쉬운 실행 방법:

```bat
run.bat
```

`run.bat`는 `front interface\dotnet\dotnet.exe`를 우선 사용합니다. SDK가 없으면 .NET 9 SDK 설치를 시도한 뒤 Release 빌드 결과인 `front interface\bin\Release\net9.0-windows\game start.exe`를 실행합니다.

개발용으로 직접 빌드하려면 다음 명령을 사용합니다.

```powershell
& ".\front interface\dotnet\dotnet.exe" build ".\front interface\RhythmGame.csproj" -c Debug
& ".\front interface\dotnet\dotnet.exe" run --project ".\front interface\RhythmGame.csproj" -c Debug
```

## 첫 화면에서 할 일

메인 화면에는 다음 진입점이 있습니다.

- `PLAY`: 곡 선택 화면으로 이동합니다.
- `RESTART`: 게임 프로그램을 다시 시작합니다.
- 우상단 기어 아이콘: 설정 화면을 엽니다.
- 좌하단 `PLAYER STATS`: 실제 플레이 횟수를 표시하고 플레이 통계 화면을 엽니다.
- 우하단 `ESC / QUIT`: 게임을 종료합니다.

처음 실행했다면 `PLAY`를 눌러 곡 선택 화면으로 들어가면 됩니다.

## 곡 선택

곡은 `front interface\Songs\InGameBGM\Original` 폴더에서 읽습니다. 지원하는 오디오 확장자는 다음과 같습니다.

- `.wav`
- `.mp3`
- `.ogg`
- `.flac`

곡 옆에 같은 이름의 JSON 파일을 두면 제목, 아티스트, BPM, 미리듣기 구간, 커버 이미지, BGA 이미지 같은 메타데이터를 지정할 수 있습니다. 자세한 형식은 [song-sidecar-schema.md](../front%20interface/docs/song-sidecar-schema.md)를 참고하세요.

곡 선택 화면에서 할 수 있는 일:

- 곡 선택 및 재생 시작
- 난이도 선택
- 4K/5K/6K/7K 레인 모드 선택
- 즐겨찾기 설정
- 곡 목록 재검색
- 곡 상세 기록 확인
- 차트 에디터 열기
- `L` 키로 현재 곡·난이도·레인에 저장된 최신 호환 리플레이 재생

곡을 선택한 뒤 난이도를 바꾸면 현재 선택한 곡을 그대로 유지합니다. 난이도순 정렬로 곡의 목록 위치가 달라지는 경우에도 같은 곡이 있는 페이지로 자동 이동합니다.

리플레이 v3는 그 플레이에 실제 사용한 차트 스냅샷과 hash, 오디오 SHA-256, 게임 버전, 오프셋, 속도, 플레이 모드를 함께 저장합니다. 이후 adaptive density가 바뀌어도 저장된 노트열로 재생하며, 곡 파일이 교체됐거나 snapshot이 손상됐거나 게임 버전이 다르면 시작 전에 차단합니다. 최신 파일이 호환되지 않으면 그보다 오래된 호환 기록을 찾고, 재생 결과가 원래 노트별 판정과 같았는지도 결과 화면에 표시합니다.

Auto 플레이는 프레임 기반 자동 입력이므로 사용자 입력 리플레이로 저장하지 않습니다. 플레이 중 속도, 시각 게임 모드, 레인 수를 바꾼 세션도 시작 설정만으로 정확히 재현할 수 없어 결과 화면에 이유를 표시하고 리플레이 저장을 건너뜁니다.

## 플레이 방법

기본 레인 입력은 다음과 같습니다.

| 모드 | 기본 키 |
|---|---|
| 4K | `D`, `F`, `J`, `K` |
| 5K | `D`, `F`, `Space`, `J`, `K` |
| 6K | `S`, `D`, `F`, `J`, `K`, `L` |
| 7K | `S`, `D`, `F`, `Space`, `J`, `K`, `L` |

플레이 중에는 `6` 또는 `Num6`으로 6K, `7` 또는 `Num7`으로 7K 전환이 가능합니다. 키 설정은 Settings의 `CONTROLS` 탭에서 바꿀 수 있습니다.

노트 종류:

- Tap: 판정선에 맞춰 한 번 누릅니다.
- Long: 시작 지점에서 누르고 끝까지 유지합니다.
- Slide: 시작 레인을 누른 뒤 끝 레인까지 이어지는 입력을 유지합니다.

판정은 `PERFECT`, `GREAT`, `BETTER`, `GOOD`, `BAD`, `MISS`로 나뉩니다. 입력이 빠른지 늦은지도 `EARLY`, `LATE`, `SYNC`로 표시됩니다.

Long/Slide 도중 일시정지했다가 돌아오면 짧은 재입력 유예 시간이 적용됩니다. 재개 후 유지해야 하는 레인 키를 다시 눌러 주세요.

## 결과와 기록

플레이가 끝나면 결과 화면에서 다음 정보를 확인합니다.

- 점수
- 정확도
- 등급: `S+`, `S`, `A`, `B`, `C`, `D`, `F`
- 클리어 타입: `Failed`, `Clear`, `Full Combo`, `All Great+`, `Perfect`
- Max Combo
- Max Miss Streak
- Early/Late 경향
- 판정 분포
- Miss가 몰린 위치를 보여주는 타임라인
- Tap/Long/Slide의 시작, 유지, 종료 실패 요약
- 다음 플레이에서 집중할 목표
- 리플레이 재생 시 원래 결과와의 일치 여부

결과 카드의 실패 약어는 `T`=Tap, `S`=Start, `H`=Hold/Slide path, `E`=End를 뜻합니다.

곡별 기록은 `%LOCALAPPDATA%\RhythmGame\song_data.json`에 저장됩니다. 설정은 `%LOCALAPPDATA%\RhythmGame\user_settings.json`에 저장됩니다. 저장 파일은 쓰기 시 백업 파일도 함께 관리합니다.

오디오 위치 안정성, pause/resume 위치, GDI 리소스 정보는 `%LOCALAPPDATA%\RhythmGame\logs`에 기록됩니다. 오디오 로그의 `format`, `meanJitterMs`, `maxJitterMs`, `backward`, `forward`, `stalls` 값으로 WAV와 압축 포맷의 실제 장치별 차이를 비교할 수 있습니다.

게임 렌더러는 선택한 FPS 간격을 그대로 사용하며 게임 진입 때 240FPS를 강제하지 않습니다. VSync는 실제 게임 프레임에도 적용되고, 배경·BGA·원근 레인·히트존처럼 변하지 않는 요소는 한 번 렌더링한 뒤 재사용합니다. 성능 로그의 `Game draw performance sample`에는 실제 `fps`와 프레임 할당량이 함께 기록됩니다.

## 설정

Settings 화면에서는 다음 항목을 조정할 수 있습니다.

- 음악 볼륨, 미리듣기 볼륨, 효과음 볼륨
- 히트 사운드 스킨, 피치, 음소거
- 노트 속도
- 입력 오프셋
- 플레이 모드: Normal, Practice, Auto
- 레인 모드와 키 바인딩
- 해상도, 프레임레이트, 렌더 품질, VSync
- 게임 플레이 HUD는 FHD(1920x1080)를 기준으로 같은 비율로 배치되며, 판정선 아래 키와 좌우 정보 패널은 화면 안전영역 안에 유지됩니다.
- 다크 모드, 고대비, 색각 보정, 움직임 감소
- 글자 크기와 스플래시 시간

입력 타이밍이 계속 빠르거나 늦다면 Settings에서 `CALIBRATE`를 실행해 오프셋을 맞추는 것이 좋습니다.

실제 플레이의 노트 판정음은 비활성화되어 있습니다. 히트 사운드 설정은 설정 화면 미리듣기와 입력 캘리브레이션 박자에만 적용됩니다. 인게임 노트는 오디오 재생 위치와 단조 증가 시계를 함께 사용하므로 MCI 위치 값이 잠시 반복되어도 멈추지 않습니다.

4K/5K/6K/7K 키 설정은 각각 별도로 저장됩니다. 이전 버전이 `KeyBindings7K`에 잘못 저장한 6개 키 배열은 6K 설정으로 자동 복구합니다.

## 차트와 자동 생성

MuWorld는 BMS subset 형식의 차트를 사용합니다. 차트 규칙은 [chart-format.md](../front%20interface/docs/chart-format.md)에 정리되어 있습니다.

WAV 파일은 내부 분석기로 길이와 비트 후보를 분석할 수 있습니다. MP3/OGG/FLAC 분석은 `ffmpeg`가 있을 때 임시 WAV로 디코딩해서 처리합니다. `ffmpeg`가 없으면 해당 포맷은 재생은 가능해도 자동 차트 분석이 제한될 수 있습니다.

`Songs/InGameBGM/Original`에 새 곡을 넣으면 폴더 변경을 자동 감지하고 Easy/Normal/Hard와 4K/5K/6K/7K 조합의 차트를 백그라운드에서 한 번 생성해 저장합니다. 이미 모든 차트가 있는 곡은 오디오를 다시 분석하지 않습니다. 생성 중인 곡을 바로 실행하면 `CHART PREPARING`을 표시하며, 생성이 끝난 뒤에는 플레이 중 분석 없이 저장된 BMS 차트를 읽습니다. Song Select의 `rescan`도 같은 사전 생성 작업을 요청합니다.

## 테스트 실행

자체 테스트는 별도 프로젝트로 들어 있습니다.

```powershell
& ".\front interface\dotnet\dotnet.exe" build ".\front interface\Tests\MuWorld.SelfTests.csproj" -c Debug --no-restore -v:minimal
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll"
```

테스트는 점수 계산, BMS 파싱, 4K~7K 차트 정규화, 차트 생성, 설정 복구와 키 설정 migration, 곡 기록, 통계, 실제 곡 파일 탐색, 판정 타이밍과 clock 안정성, Long/Slide 실패 원인, 결과 학습 피드백, 리플레이 호환성, 레인 전환, UI 렌더링, 설정 상호작용, 장시간 엔진 시뮬레이션을 확인합니다.

## 문제 해결

- 실행 파일이 빌드되지 않으면 실행 중인 `game start.exe`를 닫고 다시 빌드하세요.
- 곡이 보이지 않으면 `front interface\Songs\InGameBGM\Original`에 오디오 파일이 있는지 확인한 뒤 Song Select에서 rescan을 실행하세요.
- 자동 차트가 생성되지 않으면 WAV 파일인지 확인하거나 `ffmpeg`가 PATH에 있는지 확인하세요.
- 입력 타이밍이 밀리면 Settings의 `CALIBRATE`와 `Input Offset`을 조정하세요.
- 리플레이가 `AUDIO CHANGED`, `CHART SNAPSHOT IS INVALID`, `GAME VERSION MISMATCH`, `FORMAT OUTDATED`로 차단되면 현재 곡과 게임 버전에서 한 번 새로 플레이해 리플레이를 다시 기록하세요.
- 오디오가 튀는 것처럼 느껴지면 `%LOCALAPPDATA%\RhythmGame\logs`의 audio clock summary를 확인하고 같은 곡을 여러 번 비교하세요.
- 화면이 끊기면 같은 로그의 `Game draw performance sample`에 기록된 실제 `fps`를 확인하세요. 1920x1080에서 HIGH/144FPS가 불안정한 장치라면 BAL/60FPS로 비교할 수 있습니다.
- 설정이나 기록이 이상하면 `%LOCALAPPDATA%\RhythmGame`의 JSON 파일과 `.bak` 파일을 확인하세요.
