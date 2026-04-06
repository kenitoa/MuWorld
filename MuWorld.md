# Rhythm Game — 프로젝트 개요 및 기술 명세

## 목차

1. [프로젝트 개요](#프로젝트-개요)
2. [기술 스택](#기술-스택)
3. [아키텍처 및 코드 흐름](#아키텍처-및-코드-흐름)
4. [핵심 기능 상세](#핵심-기능-상세)
5. [앞으로 구현할 기능](#앞으로-구현할-기능)
6. [현재 버전의 구현하지 못한 점](#현재-버전의-구현하지-못한-점)

---

## 프로젝트 개요

C# WinForms 기반의 4-key 리듬 게임.  
WAV 파일을 분석하여 자동으로 채보를 생성하고, BMS format으로 저장·로드하며,  
GDI+ Custom Rendering으로 게임 화면을 직접 그린다.

- **플랫폼**: .NET 9.0 (Windows)
- **외부 NuGet Package**: 없음 (순수 .NET Framework 내장 라이브러리만 사용)

---

## 기술 스택

| 분류 | 기술 |
|------|------|
| Language | C# (.NET 9.0-windows) |
| UI Framework | Windows Forms (GDI+ Custom Rendering) |
| Audio Playback | MCI (`winmm.dll` P/Invoke), `SoundPlayer` |
| VSync | `dwmapi.dll` → `DwmFlush()` |
| Data Persistence | `System.Text.Json` (JSON Serialization) |
| Timing | `System.Diagnostics.Stopwatch` (High-Resolution Timer) |
| Chart Format | BMS (BeMusic Source) |
| Build System | MSBuild / `dotnet build` |

---

## 아키텍처 및 코드 흐름

### 전체 구조

```
Program.cs (Entry Point)
  └─ GameForm.cs (Main Loop + Screen Manager)
       ├─ UiScreen Enum 기반 State Machine
       │    ├─ Splash → MainMenu → SongSelect → Countdown → InGame → Analyze
       │    ├─ Settings
       │    └─ Achievement → AchievementDetail
       ├─ GameEngine.cs (Judgment + Note 관리)
       ├─ AudioManager.cs (Multi-Channel Audio)
       ├─ ScoreManager.cs (Score + Combo 계산)
       └─ AchievementProgress.cs (업적 평가 + 저장)
```

### Main Loop 흐름

1. **Timer Tick** (기본 8ms, ~120 FPS) → `Stopwatch` 기반 Delta Time 계산
2. **GameEngine.Update()** 호출 → Note Y 좌표 계산, Auto-Miss 처리
3. **Invalidate()** → `OnPaint()` 호출 → 현재 `UiScreen`에 따른 Rendering
4. Delta Time은 최대 50ms로 Cap하여 프레임 점프 방지

### Rendering Pipeline

- **DoubleBuffered** 활성화
- GDI+ Object (Brush, Pen, Font)를 Static으로 Caching → Frame 당 Allocation 제거
- 게임 중에는 `HighSpeed` Rendering Hint, 메뉴에서는 `AntiAlias` 적용
- Design Resolution (1152×768) 기준으로 `ScaleX()` / `ScaleY()` 동적 Scaling

### Input 처리

| Key | 기능 |
|-----|------|
| D, F, J, K | Lane 0~3 입력 |
| 1 / 2 | Note Speed 증가/감소 (0.1x 단위, 0.5x~5.0x) |
| 3 / 4 | Game Mode 순환 (Normal → Blind → Fog) |
| ESC | 뒤로가기 / 게임 종료 |
| Space / Enter | 선택 / 게임 시작 |
| Alphanumeric | 곡 검색 입력 |

Key Repeat 방지를 위해 `_lanePressed[]` Boolean Array로 KeyDown/KeyUp State 관리.

---

## 핵심 기능 상세

### 1. Time-Based Judgment System

Position(Y 좌표)이 아닌 **시간 차이**로 판정:

| Judgment | Window |
|----------|--------|
| PERFECT | ±30ms |
| GREAT | ±60ms |
| BETTER | ±90ms |
| GOOD | ±120ms |
| BAD | ±150ms |
| MISS | >180ms (Auto) |

- Note Speed와 Judgment가 완전히 분리 → Speed 변경이 판정에 영향을 주지 않음
- Y 좌표는 `hitCenterY - (remainingTime × noteSpeed)` 공식으로 역산하여 시각적으로만 사용

### 2. Audio Analysis & Beat Detection

`WavAnalyzer.cs`에서 WAV 파일의 Beat를 자동 감지:

1. PCM Data를 Mono Float Array로 Convert (8/16/24/32-bit 지원)
2. ~23ms Window (1024 samples @ 44.1kHz), 50% Overlap으로 RMS Energy 계산
3. Local Average (±0.5s) 대비 Threshold (`localAvg × 1.4`) 초과 시 Beat로 판정
4. 인접 Frame보다 에너지가 높고, 이전 Beat와 최소 80ms 간격일 때만 확정

### 3. Procedural Chart Generation

`ChartGenerator.cs`에서 Beat 분석 결과를 기반으로 채보 자동 생성:

**Difficulty별 설정:**

| Difficulty | Notes/sec | Subdivision |
|------------|-----------|-------------|
| Easy | 1 | 8th Note |
| Normal | 6 | 16th Note |
| Hard | 9 | 24th Note |

**7가지 Pattern Type:**

| Pattern | 설명 |
|---------|------|
| Stream | 순차 흐름 (0→1→2→3→2→1) |
| Stair | 한 방향 계단식 |
| Trill | 두 Lane 빠른 교차 |
| Jack | 같은 Lane 반복 |
| Zigzag | 양 끝 교차 (0↔3↔1↔2) |
| Swing | 인접 Lane 교차 |
| Spread | 중심↔바깥 확산 |

- Difficulty별 Pattern 가중치 차등 적용 (Easy: Stream 50%, Hard: Trill 20% 등)
- Lane Balance 관리: 동일 Lane 3회 연속 방지, 25% 초과 Lane 보정
- 생성 결과를 BMS Format으로 저장

### 4. BMS Chart Loader

`NoteLane.cs`에서 BMS 파일 Parse:

- Channel Mapping: `11`→Lane 0, `12`→Lane 1, `13`→Lane 2, `14`→Lane 3
- Hex Notation Parsing (예: `"0100"` → 4칸 중 2번째에 Note)
- Time 계산: `(measure + offset) × (240 / BPM)`
- Fallback: 생성 채보 → default.bms → Procedural Fallback 순서

### 5. Multi-Channel Audio System

`AudioManager.cs`에서 Thread-Safe한 Audio 관리:

| Channel | 방식 | 용도 |
|---------|------|------|
| Main Screen BGM | MCI (mciSendString) | 메인 화면 루프 재생 |
| In-Game BGM | MCI (mciSendString) | 게임 플레이 중 곡 재생 |
| Hit Sound (SFX) | PlaySound / SoundPlayer | 판정 시 효과음 |

- **Hit Sound 합성**: `CreateDualToneWavBytes()` 로 Dual-Tone WAV를 Runtime 생성
  - PERFECT: 990Hz + 1980Hz / GOOD: 760Hz + 1520Hz
  - 44.1kHz, 16-bit Mono
- 모든 Method에 `lock (_sync)` 적용 → Thread Safety
- Volume: MCI 0-1000 Scale, SFX는 Amplitude 기반 조정

### 6. Game Mode (시각 효과)

| Mode | 효과 |
|------|------|
| Normal | 기본 |
| Blind | 화면 상단을 가림, Hit Zone 근처만 보임 |
| Fog | 전체 가시성 감소 + Hit Zone 근처 Gradient Fade |

`ApplyGameModeEffect()` 에서 Semi-Transparent Brush로 Overlay Rendering.

### 7. Scoring System

`ScoreManager.cs`:

```
Score += JudgmentPoints × CurrentCombo
```

| Judgment | Points |
|----------|--------|
| Perfect | 300 |
| Great | 250 |
| Better | 150 |
| Good | 100 |
| Bad | 50 |

- Hit 시 Combo 증가, Miss 시 Combo 0으로 Reset
- MaxCombo 별도 추적

### 8. Achievement System

`AchievementProgress.cs` — 3-Tier, 총 70개 업적:

| Tier | 이름 | 개수 |
|------|------|------|
| 0 | Bronze Achiever | 20 |
| 1 | Silver Achiever | 25 |
| 2 | Star Player | 25 |

**업적 조건 카테고리:**

- Play Count (1~100회)
- Highest Score (5K~150K)
- Combo Milestone (5~150)
- Perfect Count (5~300)
- Total Score 누적 (5K~500K)
- Note 처리 수 (20~2K)
- Missless 연속 게임 (2~20회)
- Judgment 분포 (GOOD Count 등)

Progress는 `%LOCALAPPDATA%/RhythmGame/player_progress.json`에 JSON으로 Persist.  
게임 종료 시 `ApplySession()`으로 일괄 평가, 해금 시 Toast Notification 표시.

### 9. UI 화면 구성

| Screen | 파일 | 기능 |
|--------|------|------|
| Main Menu | GameForm.cs | 시작 화면, 메뉴 Navigation |
| Song Select | GameForm_song_select.cs | 곡 목록, 검색, Difficulty Tab, Paging |
| Settings | GameForm_settings.cs | FPS, Volume, Dark Mode, Lane Brightness |
| In-Game | GameForm.cs | 실시간 게임 Rendering |
| Analyze | GameForm_analyze.cs | 결과 화면 (Score, Combo, Judgment 통계) |
| Achievement | GameForm_Achievement.cs | 업적 Tier Card 목록 |
| Achievement Detail | GameForm_Achievement_detail.cs | 개별 업적 Progress |

### 10. 성능 최적화

- **Static GDI+ Object Pooling**: Brush, Pen, Font를 Static Field로 미리 생성
- **DoubleBuffering**: Flicker 방지
- **Delta Time Capping**: 50ms 상한으로 프레임 급변 시 안정성 확보
- **Fisher-Yates Shuffle**: LINQ Allocation 없는 Lane 섞기
- **조건부 Rendering Hint**: 게임 중 `HighSpeed`, 메뉴에서 `AntiAlias`
- **FPS 설정**: 30 / 60 / 120 / 144 / 240 선택 가능
- **VSync**: `DwmFlush()` P/Invoke

---

## 앞으로 구현할 기능

- **Long Note (Hold Note)**: 길게 누르는 Note Type 추가
- **Online Leaderboard**: Score 온라인 등록·조회 시스템
- **Custom Key Binding**: 유저가 Lane Key를 자유롭게 설정
- **Replay System**: 플레이 입력을 기록하고 재생
- **Multiplayer Mode**: 로컬 또는 네트워크 대전
- **Skin / Theme Customization**: Note, Background, Hit Effect 등 시각 요소 커스텀
- **Chart Editor**: 유저가 직접 채보를 만들고 편집하는 에디터
- **OGG / MP3 Format 지원**: 현재 WAV 전용인 Audio Input을 다양한 Format으로 확장
- **Offset Calibration**: Audio-Visual Sync를 유저가 미세 조정할 수 있는 설정
- **BGA (Background Animation)**: 게임 플레이 중 배경 영상 재생
- **Combo / Judgment Hit Effect Animation**: 판정 시 파티클이나 애니메이션 연출
- **Accuracy / Rate 표시**: 전체 정확도를 백분율이나 Grade (S, A, B 등)로 표시
- **Pause / Resume**: 게임 중 일시정지 및 재개 기능
- **곡별 High Score 기록 저장**: 곡 + Difficulty 단위로 최고 점수 Persist

---

## 현재 버전의 구현하지 못한 점

### Audio 관련
- **WAV Format만 지원**: MP3, OGG, FLAC 등은 입력 불가
- **Synthetic BGM Legacy Code 잔존**: `AudioManager.RestartBgmCore()` 에 사용하지 않는 합성 BGM 코드가 남아있음
- **Hit Sound Variety 부족**: Perfect / Good 두 종류의 SFX만 존재, Judgment별 차별화 미흡
- **Audio Latency 보정 없음**: 시스템별 Audio Output Delay에 대한 Offset 보정 기능 미구현

### Gameplay 관련
- **Lane 수 고정 (4 Lane)**: 5K, 7K 등 다른 Key 모드 미지원
- **Note Type 단일**: Tap Note만 존재, Long Note / Slide Note 없음
- **Pause 기능 없음**: 게임 중 일시정지 불가, ESC는 즉시 종료
- **Speed 변경이 게임 중 미반영**: 게임 시작 전에만 Speed 설정 가능 (실시간 변동 미지원 가능성)
- **Miss Streak 근사치 계산**: `MissCount / (HitCount + 1)` 공식으로 처리하여 실제 연속 Miss 횟수와 상이

### UI / UX 관련
- **해상도 대응 한계**: Design Resolution (1152×768)을 기준으로 Scaling하지만, 극단적 종횡비에서의 Layout 보장 없음
- **Splash Screen 하드코딩**: 타이밍과 Duration이 고정값
- **접근성(Accessibility) 미지원**: Screen Reader, 고대비 모드, 색약 지원 등 미구현
- **UI Framework 한계**: WinForms + GDI+ 기반이므로 GPU Accelerated Rendering 미사용

### Data 관련
- **곡별 Score Persist 없음**: 게임 세션 종료 시 Score가 Achievement 집계에만 반영, 곡별 최고 점수 기록 미저장
- **곡 Metadata 부족**: Artist, BPM, 곡 길이 등의 정보가 파일 이름에만 의존
- **설정 Persist 미확인**: FPS, Volume, Dark Mode 등의 유저 설정이 세션 간 유지되는지 불명확

### Chart Generation 관련
- **Beat Detection 정확도**: 단순 RMS Energy 기반이므로, 복잡한 장르(EDM Drop, Polyrhythm 등)에서 Beat 감지 정확도 저하 가능
- **BPM 변속 미지원**: 곡 내 BPM 변화를 처리하지 못함 (단일 BPM 가정)
- **채보 난이도 자동 조절 없음**: 유저 실력에 따른 Dynamic Difficulty Adjustment 없음
- **생성된 채보의 수동 편집 불가**: Chart Editor 미구현으로 Auto-Generated 결과만 사용 가능
