# MuWorld 기술 문서

이 문서는 MuWorld의 코드 구조와 주요 구현 방식을 정리합니다. 사용자용 실행 설명은 [ReadMe.md](ReadMe.md), 앞으로의 개선 항목은 [addMe.md](addMe.md)를 기준으로 봅니다.

아래 소스 경로는 별도 표기가 없으면 저장소의 `front interface` 폴더를 기준으로 합니다.

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

Song Select의 난이도 변경은 목록 인덱스를 보존하지 않고 선택 곡의 `SongId`를 보존합니다. 난이도 레벨 정렬로 목록 순서가 바뀐 뒤에는 같은 `SongId`를 다시 찾아 선택 인덱스와 페이지를 함께 복원합니다.

## 게임 루프와 시간 기준

게임 루프는 WinForms `Timer`와 `Stopwatch`를 사용합니다. 프레임 delta는 50ms로 제한해 긴 프레임이 노트 위치를 크게 튀게 하지 않도록 합니다.

실제 플레이 중 chart clock은 `AudioManager.GetInGameBgmPositionSeconds()`의 오디오 재생 위치를 기준으로 사용합니다. 동기 MCI position 조회는 UI thread 정지를 줄이기 위해 50ms 간격으로 제한하고, 조회 사이에는 `Stopwatch` 기반 단조 증가 시계로 위치를 예측합니다. 새 오디오 sample이 앞으로 이동하면 그 위치를 채택하고, 같거나 오래된 sample이면 frame delta로 계속 진행하므로 판정 clock이 멈추거나 뒤로 가지 않습니다. 오디오 위치가 `NaN`/`Infinity`인 경우에도 frame delta가 fallback 역할을 합니다. 시각 clock은 이 판정 clock을 별도로 보간합니다.

입력 오프셋은 `AudioOffsetMs`로 저장되며, 노트 spawn, 위치, 판정이 같은 chart clock을 공유하도록 적용됩니다.

pause 중에는 lane key release가 판정을 확정하지 않습니다. resume 시 `GameEngine.GrantHoldResumeGrace`가 짧은 재입력 시간을 제공해 Long/Slide가 첫 frame에 즉시 miss가 되는 것을 막습니다.

## 입력과 레인 모드

지원 레인 모드는 4K, 5K, 6K, 7K입니다.

기본 키:

- 4K: `D`, `F`, `J`, `K`
- 5K: `D`, `F`, `Space`, `J`, `K`
- 6K: `S`, `D`, `F`, `J`, `K`, `L`
- 7K: `S`, `D`, `F`, `Space`, `J`, `K`, `L`

키 바인딩은 Settings의 `CONTROLS` 흐름에서 바꿀 수 있습니다. `UserSettings`는 `KeyBindings4K`, `KeyBindings5K`, `KeyBindings6K`, `KeyBindings7K`를 각각 저장합니다. 구 버전의 저장 오류로 `KeyBindings7K`에 들어간 길이 6 배열은 6K 설정으로 읽고, 길이 7 배열만 7K 설정으로 읽습니다. 잘못된 길이, 중복 키, 예약 키, 파싱할 수 없는 키는 해당 모드의 기본 배열로 안전하게 fallback합니다.

## 노트와 판정

`NoteType`은 Tap, Long, Slide를 모델링합니다.

- Tap은 단일 판정을 가집니다.
- Long은 시작 판정과 hold 유지, 끝 판정이 분리됩니다.
- Slide는 시작 lane과 end lane을 함께 관리합니다.

판정은 시간 차이에 따라 `PERFECT`, `GREAT`, `BETTER`, `GOOD`, `BAD`, `MISS`로 계산됩니다. `ScoreManager`는 점수, 정확도, combo, max combo, miss streak, grade, clear type을 계산합니다.

점수는 1,000,000점 만점 정규화 방식입니다. Accuracy는 판정별 가중치를 사용하며, 게임 HUD, 결과 화면, 곡 기록이 같은 계산 결과를 공유해야 합니다.

`GameEngine.JudgmentHistory`에는 각 판정의 차트 시각, 레인, 노트 종류, Start/Hold/End phase, offset, 실패 원인이 구조화되어 기록됩니다. miss를 `TapMiss`, `LongStartMiss`, `LongHoldBreak`, `LongEndMiss`, `SlideStartMiss`, `SlidePathBreak`, `SlideEndMiss`로 나누며, event 추가와 `ScoreManager.AddMiss()`가 같은 resolve 경로에서 실행됩니다.

`ResultFeedbackSummary`는 이 event와 `ScoreManager` 집계를 사용해 Early/Late/Stable 요약, miss 위치 rail, Start/Hold/End 실패 수, 다음 플레이 목표를 만듭니다. Analyze 화면은 BAD를 포함한 전체 판정 분포와 실제 clear type도 표시합니다.

## 게이지와 플레이 모드

플레이 모드는 다음과 같습니다.

- Normal: groove gauge가 실패 조건과 clear threshold에 영향을 줍니다.
- Practice: 실패 없이 끝까지 연습할 수 있습니다.
- Auto: 노트를 자동 판정합니다.

난이도별로 시작 gauge, clear threshold, hit gain, bad/miss loss가 다르게 적용됩니다.

## 오디오 시스템

현재 BGM과 preview는 MCI alias를 사용합니다. hit sound는 BGM과 독립된 경로이지만 실제 게임 판정에서는 호출하지 않으며, Settings 미리듣기와 입력 calibration 박자에만 사용합니다.

`AudioClockDiagnostics`는 인게임 위치 sample과 wall clock 차이를 누적합니다. 재생 종료 시 포맷, sample 수, query 실패, 역행, 정방향 jump, stall, 평균/최대 jitter, 마지막 연속 구간 drift를 `%LOCALAPPDATA%/RhythmGame/logs`에 기록합니다. pause/resume은 별도 segment로 분리해 pause 시간을 drift로 오인하지 않습니다. 인게임 BGM의 MCI open/play/volume/pause/resume/stop 실패 코드도 같은 로그에 남습니다. Main BGM과 preview 명령 전체의 오류 계측은 아직 이 범위에 포함하지 않습니다.

지원 탐색 확장자:

- `.wav`
- `.mp3`
- `.ogg`
- `.flac`

분석 경로:

- WAV는 내부 `WavAnalyzer`가 직접 duration, RMS, transient, beat 후보를 계산합니다.
- MP3/OGG/FLAC는 `ffmpeg`가 있을 때 임시 PCM WAV로 디코딩한 뒤 분석합니다.
- `ffmpeg`가 없으면 비-WAV 자동 분석은 제한됩니다.

`FileSystemWatcher`가 `Songs/InGameBGM/Original`의 오디오 및 sidecar 변경을 감지하고 900ms debounce 후 차트 생성을 요청합니다. 생성기는 곡당 분석을 한 번만 수행해 Easy/Normal/Hard × 4K/5K/6K/7K의 12개 lane-specific BMS를 임시 파일에 쓴 뒤 atomic move로 확정합니다. 12개 조합이 이미 존재하면 오디오 분석 자체를 건너뜁니다. 생성 도중 추가 변경이 들어오면 요청 플래그를 유지해 현재 작업 직후 다시 탐색하며, 플레이 시작은 해당 곡의 사전 계산이 진행 중일 때 `CHART PREPARING` 상태로 대기합니다.

재생 자체는 현재 모든 포맷을 Windows MCI `mpegvideo` backend로 엽니다. 따라서 WAV와 MP3/OGG/FLAC의 position precision은 Windows codec/device 구성에 따라 달라질 수 있으며, 특정 차이를 코드에서 가정하지 않고 위 telemetry의 `format`별 반복 측정으로 판단합니다.

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
| `replays/*.json` | replay/game/chart version, 실제 플레이 차트 snapshot, 입력 이벤트, settings snapshot, 원래 결과와 노트별 판정 |

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

정규화는 4K/5K/6K/7K별 유효 레인 순서를 사용합니다. 6K가 7K lane index를 만들어 노트를 잃지 않도록 별도 spread 순서를 둡니다. validator는 유한하지 않은 time/duration과 알 수 없는 note type도 제거합니다. 난이도 분석의 chord 탐색은 정렬된 sliding window를 사용해 큰 차트에서 반복 전체 검색을 피합니다.

## 리플레이 계약

새 리플레이 형식은 version `3`입니다.

- 시작 전 replay format, release assembly version, song, difficulty, lane count, chart hash를 비교합니다. 현재 replay engine 기준은 `1.1.0`이며 판정 규칙이 호환되지 않게 바뀌면 project version과 replay format을 함께 올립니다.
- 실제 플레이에 사용한 정렬된 `LaneNote` snapshot을 저장하고 자체 hash를 검증하므로 adaptive density 갱신 뒤에도 같은 노트열을 재생합니다.
- 곡 파일 전체의 SHA-256을 기록하고 재생 직전에 현재 파일과 비교해 같은 경로의 오디오가 교체된 경우도 차단합니다. 일반 플레이의 hash 계산은 3초 countdown 동안 background에서 수행합니다.
- replay 후보 JSON 탐색과 SHA-256은 UI thread 밖에서 수행하고, 선택 곡·난이도·레인이나 게임 시작 상태가 바뀌면 완료된 비동기 요청을 폐기합니다. 같은 파일에 대해 동시에 요청된 hash 작업만 공유하고 완료 뒤 cache에서 제거합니다.
- offset, note speed, play mode, visual game mode snapshot을 재생 중에만 적용하고 현재 사용자 설정은 바꾸지 않습니다.
- 입력 time이 유한하고 정렬됐는지, lane이 범위 안인지 검증합니다.
- 종료 후 score, accuracy, max combo/miss streak, grade, clear type과 PERFECT/GREAT/BETTER/GOOD/BAD/MISS 전체 분포 및 노트별 의미 판정을 원본과 비교합니다. UI frame에 따라 달라지는 sampling 시각은 비교 대상에서 제외합니다.
- 플레이 중 speed, game mode, lane 수를 바꾼 세션은 시작 snapshot만으로 재현할 수 없으므로 오해를 부르는 replay를 저장하지 않고 결과 화면에 이유를 표시합니다.
- Auto 플레이는 timer가 판정을 생성하고 입력 event stream이 재현 근거가 아니므로 replay 저장 대상에서 제외합니다.
- 호환되지 않는 구형 replay, 손상된 chart snapshot, 교체된 audio는 명시적 이유와 함께 시작 전에 차단합니다.
- 결과가 같으면 `REPLAY VERIFIED`, 다르면 `REPLAY MISMATCH`를 Analyze 화면과 로그에 남깁니다.

## UI와 접근성

UI는 대부분 custom drawing입니다. 실제 WinForms 컨트롤을 많이 배치하지 않기 때문에 `GameForm_accessibility.cs`가 virtual accessible node 목록을 만들어 screen reader와 keyboard navigation을 보완합니다.

Settings, Statistics, Key Bindings, Input Calibration 계열의 주 제목은 같은 시각 크기 범위를 사용합니다. Analyze 화면처럼 제목이나 등급 양옆에 장식선을 그리는 경우 고정 좌표로 글자 위를 통과시키지 않고, 실제 렌더링된 글자 폭을 측정해 여백 밖의 두 구간에만 선을 그립니다. 기본 화면 검증은 1366x768과 1920x1080 캡처를 포함합니다.

게임 플레이 화면은 1920x1080을 디자인 기준으로 사용합니다. 앨범 아트, 진행 바, 난이도 배지, 속도/점수 패널, 판정선과 레인 키는 동일한 gameplay scale로 계산하며, 판정선의 하단 여백도 화면 높이에 비례합니다. 해상도 smoke test는 각 HUD 사각형과 레인 키가 viewport 밖으로 잘리지 않고 진행 바와 점수 패널이 겹치지 않는지 검사합니다.

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

게임 루프는 Settings의 프레임 간격을 유지하며 low-latency 진입 시 `timeBeginPeriod(1)`만 적용합니다. 이전처럼 타이머를 무조건 4ms로 바꾸지 않습니다. BGM 종료 판정은 알려진 재생 길이와 단조 증가 오디오 clock으로 계산해 매 프레임 MCI status 명령을 보내지 않습니다. Paint 경로도 tick에서 받은 마지막 오디오 위치를 사용하므로 렌더 중 별도 MCI 호출이 없습니다.

정적 게임 배경, BGA 합성, 산/도로, 기본 원근 레인과 hit zone은 크기·테마·레인·skin을 키로 하는 `Format32bppPArgb` bitmap cache에 한 번 그린 뒤 재사용합니다. 매 프레임에는 별 효과, 눌린 레인 강조, 노트, HUD만 갱신합니다. 자체 렌더 벤치마크의 1366x768 Debug 기준 정적 게임 프레임 평균은 캐시 확장 전 23.16ms, 확장 후 8.20ms였습니다. 실제 장치 성능은 `%LOCALAPPDATA%/RhythmGame/logs`의 `Game draw performance sample`에 `fps`로 기록합니다.

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

추가 회귀 범위에는 4K~7K 키 설정 round-trip과 legacy 6K migration, 전 레인 차트 정규화, audio clock 역행/비정상 값, pause hold grace, 구조화 miss 원인, 결과 학습 피드백, replay v3 호환성/결과 비교가 포함됩니다.

권장 검증 명령:

```powershell
& ".\front interface\dotnet\dotnet.exe" build ".\front interface\Tests\MuWorld.SelfTests.csproj" -c Debug --no-restore -v:minimal
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll"
```

Release 검증은 실행 중인 `front interface\bin\Release\net9.0-windows\game start.exe`가 있으면 파일 잠금으로 실패할 수 있습니다. Release build 전에는 실행 중인 앱을 종료해야 합니다.
