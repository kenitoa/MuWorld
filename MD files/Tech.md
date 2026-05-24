# MuWorld 기술 문서

이 문서는 MuWorld의 코드 구조와 주요 구현 방식을 정리합니다. 사용자용 실행 설명은 [ReadMe.md](ReadMe.md), 앞으로의 개선 항목은 [addMe.md](addMe.md)를 기준으로 봅니다.

## 프로젝트 구성

| 영역 | 주요 파일 | 역할 |
|---|---|---|
| 앱 진입점 | `Core/Program.cs` | WinForms 애플리케이션 시작 |
| 메인 폼 | `Forms/GameForm.cs` | 화면 상태, 게임 루프, 입력, 공통 렌더링 |
| 화면별 UI | `Forms/GameForm_*.cs`, `Forms/timing_UI.cs` | Settings, Song Select, Analyze, Achievement, Calibration, Key Binding, Chart Editor |
| 게임 엔진 | `Core/GameEngine.cs`, `Core/Note.cs` | 노트 상태, 판정, hold/slide 처리, lane switching |
| 차트 | `Chart/NoteLane.cs`, `Chart/ChartGenerator.cs`, `Chart/ChartValidation.cs` | BMS subset 파싱, 자동 생성, 검증 |
| 오디오 | `Audio/AudioManager.cs`, `Audio/AudioFileCatalog.cs`, `Audio/AudioAnalysisPipeline.cs`, `Audio/WavAnalyzer.cs` | BGM/preview/SFX 재생, 곡 탐색, 분석 |
| 데이터 | `Data/*.cs` | 설정, 점수, 곡 기록, 업적, 입력 로그, 리플레이 |
| 테스트 | `Tests/Program.cs`, `Tests/MuWorld.SelfTests.csproj` | 실행 가능한 자체 검증 |

대상 프레임워크는 `net9.0-windows`이고 UI는 WinForms custom drawing 기반입니다. 실행 어셈블리 이름은 `game start`입니다.

## 화면 상태

`UiScreen`은 다음 상태를 가집니다.

- `Splash`
- `MainMenu`
- `Settings`
- `SongSelect`
- `SongDetail`
- `Achievement`
- `AchievementDetail`
- `Analyze`
- `InputCalibration`
- `KeyBindings`
- `ChartEditor`

`GameForm.OnPaint`가 현재 상태에 맞는 draw 메서드를 호출합니다. 메인 메뉴, 곡 선택, 설정, 결과 화면은 각각 partial 파일로 책임이 분리되어 있습니다.

## 게임 루프와 시간 기준

게임 루프는 WinForms `Timer`와 `Stopwatch`를 사용합니다. 프레임 delta는 50ms로 제한해 긴 프레임이 노트 위치를 크게 튀게 하지 않도록 합니다.

실제 플레이 중 chart clock은 가능한 경우 `AudioManager.GetInGameBgmPositionSeconds()`의 오디오 재생 위치를 기준으로 사용합니다. 오디오 위치를 사용할 수 없거나 BGM이 없는 상황에서는 `Stopwatch` 기반 누적 시간이 fallback 역할을 합니다.

입력 오프셋은 `AudioOffsetMs`로 저장되며, 노트 spawn, 위치, 판정이 같은 chart clock을 공유하도록 적용됩니다.

## 입력과 레인 모드

지원 레인 모드는 4K, 5K, 6K, 7K입니다.

기본 키:

- 4K: `D`, `F`, `J`, `K`
- 5K: `D`, `F`, `Space`, `J`, `K`
- 6K: `S`, `D`, `F`, `J`, `K`, `L`
- 7K: `S`, `D`, `F`, `Space`, `J`, `K`, `L`

키 바인딩은 Settings의 `CONTROLS` 흐름에서 바꿀 수 있습니다. 현재 `UserSettings` 스키마는 `KeyBindings4K`, `KeyBindings5K`, `KeyBindings7K`를 저장합니다. 6K 기본 입력은 코드에 존재하지만, 6K 전용 저장 필드는 별도로 분리되어 있지 않습니다. 이 항목은 [addMe.md](addMe.md)의 필수 수정 항목입니다.

## 노트와 판정

`NoteType`은 Tap, Long, Slide를 모델링합니다.

- Tap은 단일 판정을 가집니다.
- Long은 시작 판정과 hold 유지, 끝 판정이 분리됩니다.
- Slide는 시작 lane과 end lane을 함께 관리합니다.

판정은 시간 차이에 따라 `PERFECT`, `GREAT`, `BETTER`, `GOOD`, `BAD`, `MISS`로 계산됩니다. `ScoreManager`는 점수, 정확도, combo, max combo, miss streak, grade, clear type을 계산합니다.

점수는 1,000,000점 만점 정규화 방식입니다. Accuracy는 판정별 가중치를 사용하며, 게임 HUD, 결과 화면, 곡 기록이 같은 계산 결과를 공유해야 합니다.

## 게이지와 플레이 모드

플레이 모드는 다음과 같습니다.

- Normal: groove gauge가 실패 조건과 clear threshold에 영향을 줍니다.
- Practice: 실패 없이 끝까지 연습할 수 있습니다.
- Auto: 노트를 자동 판정합니다.

난이도별로 시작 gauge, clear threshold, hit gain, bad/miss loss가 다르게 적용됩니다.

## 오디오 시스템

현재 BGM과 preview는 MCI alias를 사용합니다. hit sound는 BGM과 독립된 경로로 재생해 동시 재생을 보장합니다.

지원 탐색 확장자:

- `.wav`
- `.mp3`
- `.ogg`
- `.flac`

분석 경로:

- WAV는 내부 `WavAnalyzer`가 직접 duration, RMS, transient, beat 후보를 계산합니다.
- MP3/OGG/FLAC는 `ffmpeg`가 있을 때 임시 PCM WAV로 디코딩한 뒤 분석합니다.
- `ffmpeg`가 없으면 비-WAV 자동 분석은 제한됩니다.

## 곡 데이터와 메타데이터

곡은 `Songs/InGameBGM/Original`에서 탐색합니다. `AudioFileCatalog`는 파일명 기반 legacy ID와 경로 hash suffix가 붙은 song ID를 생성합니다. 같은 파일명이라도 경로가 다르면 충돌을 줄일 수 있습니다.

sidecar JSON은 `docs/song-sidecar-schema.md`를 따릅니다. 주요 필드는 다음과 같습니다.

- `title`
- `artist`
- `bpm`
- `durationSeconds`
- `previewStart`
- `previewEnd`
- `genre`
- `source`
- `bga`
- `cover`

## 저장 위치

앱 데이터는 기본적으로 `%LOCALAPPDATA%\RhythmGame` 아래에 저장됩니다.

| 파일 | 내용 |
|---|---|
| `user_settings.json` | 볼륨, 오프셋, 화면, 접근성, 키 설정 등 |
| `song_data.json` | 곡별 최고 기록, history, 즐겨찾기, adaptive density |
| `player_progress.json` | 업적과 누적 진행 |
| `input_logs` | 플레이 중 입력 이벤트 로그 |
| replay 저장 파일 | chart version, song ID, 입력 이벤트, offset, speed, lane mode |

저장소는 `SafeJsonFile.WriteWithBackup` 흐름을 사용해 `.bak` 복구를 시도합니다.

## 차트 시스템

차트 형식은 BMS subset입니다. 자세한 문법은 `docs/chart-format.md`를 기준으로 합니다.

로드 우선순위는 대략 다음 흐름입니다.

1. 사용자 편집 lane-specific chart
2. legacy 사용자 chart
3. 생성된 lane-specific chart
4. 생성된 legacy chart
5. 기본 chart fallback

`ChartGenerator`는 Easy/Normal/Hard 차트를 생성하고, lane-specific 파일명에는 `_4k`, `_5k`, `_6k`, `_7k` suffix를 사용합니다.

`ChartValidation`은 최소 간격, 같은 lane 겹침, long/slide duration, slide end lane 유효성, density 같은 조건을 검증합니다.

## UI와 접근성

UI는 대부분 custom drawing입니다. 실제 WinForms 컨트롤을 많이 배치하지 않기 때문에 `GameForm_accessibility.cs`가 virtual accessible node 목록을 만들어 screen reader와 keyboard navigation을 보완합니다.

지원 항목:

- Tab/Shift+Tab 또는 방향키 기반 focus 이동
- Enter/Space 실행
- slider와 segmented control의 Left/Right 조정
- 고대비 모드 색상 분기
- 색각 보정 palette
- Reduced Motion
- Text Size

## 렌더링 리소스와 성능

인게임 렌더링은 `RenderResourceCache`를 통해 반복 생성되는 Font/Brush/Pen 비용을 줄입니다. `GdiResourceMonitor`는 주요 흐름에서 GDI object count를 샘플링해 누수를 추적합니다.

게임 중 low-latency timer 모드는 `timeBeginPeriod(1)`을 best-effort로 호출하고, 종료 시 `timeEndPeriod(1)`로 되돌립니다.

## 테스트

자체 테스트 프로젝트는 `Tests/MuWorld.SelfTests.csproj`입니다.

현재 테스트 범위:

- ScoreManager 계산
- BMS parse와 validation
- ChartGenerator 파일명과 tempo map
- UserSettingsStore backup recovery
- SongDataStore lane-mode records/history
- Statistics snapshot
- 실제 song file 탐색
- 판정 타이밍 시뮬레이션
- Long/Slide note behavior
- Combo, speed, live lane switching
- Perspective note lane alignment
- Analyze layout bounds
- UI smoke and resolution draw
- Settings pages render and interact
- 10-minute engine simulation

권장 검증 명령:

```powershell
.\dotnet\dotnet.exe build .\Tests\MuWorld.SelfTests.csproj -c Debug --no-restore -v:minimal
.\dotnet\dotnet.exe .\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll
```

Release 검증은 실행 중인 `bin\Release\net9.0-windows\game start.exe`가 있으면 파일 잠금으로 실패할 수 있습니다. Release build 전에는 실행 중인 앱을 종료해야 합니다.
