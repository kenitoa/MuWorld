# MuWorld 리듬게임 구현 점검표

이 문서는 현재 코드 기준으로 MuWorld가 이미 갖춘 기능과, 리듬게임으로 완성도를 높이기 위해 추가하거나 다듬어야 할 요소를 정리한 작업 목록이다.

## 현재 코드 기준 구현 현황

| 영역 | 현재 상태 | 관련 파일 |
|---|---|---|
| 게임 루프 | WinForms `Timer`와 `Stopwatch` 기반 프레임 루프, 50ms delta cap, 더블 버퍼링 | `Forms/GameForm.cs` |
| 화면 상태 | Splash, Main Menu, Song Select, Settings, Achievement, Analyze 결과 화면 | `Forms/GameForm*.cs` |
| 레인 모드 | 4K, 5K, 7K 입력 배열 존재. 5/6 키로 레인 모드 전환 | `Forms/GameForm.cs`, `Forms/timing_UI.cs` |
| 노트 타입 | Tap, Long, Slide 타입 모델 존재. Long hold 판정 일부 구현 | `Core/Note.cs`, `Core/GameEngine.cs`, `Chart/NoteLane.cs` |
| 판정 | 시간 기반 PERFECT/GREAT/BETTER/GOOD/BAD/MISS 판정 | `Core/GameEngine.cs` |
| 점수 | 1,000,000점 만점 정규화 점수, Accuracy/Grade/Clear Type, Max Combo, Miss Streak 집계 | `Data/ScoreManager.cs` |
| 게이지 | Groove gauge, Normal/Practice 모드, 난이도별 clear threshold와 실패 판정 | `Forms/GameForm.cs`, `Forms/GameForm_settings.cs` |
| 배속 | 표시 1.0x가 내부 기존 4배속 기준. 0.1 단위, 0.1~5.0 표시 범위 | `Forms/timing_UI.cs` |
| 일시정지 | ESC/P로 Pause, ESC/P로 Resume, Back으로 종료 | `Forms/GameForm.cs`, `Audio/AudioManager.cs` |
| 오디오 | MCI 기반 BGM/Preview alias, 독립 preview volume, hit sound skin/pitch/mute, 음악 종료 감지 | `Audio/AudioManager.cs`, `Audio/AudioEngineCatalog.cs` |
| 곡 탐색/분석 | WAV/MP3/OGG/FLAC 파일 탐색, WAV PCM 분석, ffmpeg 기반 비-WAV 분석, sidecar JSON metadata 일부 지원 | `Audio/AudioFileCatalog.cs`, `Audio/AudioAnalysisPipeline.cs` |
| 곡 라이브러리 | sidecar schema, 경로 hash 기반 song ID, cover artwork, 정렬/필터/rescan, 곡 상세 기록 | `Audio/AudioFileCatalog.cs`, `Forms/GameForm_song_select.cs`, `Forms/GameForm_song_detail.cs` |
| WAV 분석 | WAV duration, RMS/Transient 기반 beat 후보 검출 | `Audio/WavAnalyzer.cs` |
| 채보 생성 | WAV 분석 기반 Easy/Normal/Hard BMS 자동 생성 | `Chart/ChartGenerator.cs` |
| 채보 시스템 | BMS subset 검증, lane-specific chart, 내부 Chart Editor, density preview, Lv.1~15 산출 | `Chart/*`, `Forms/GameForm_chart_editor.cs` |
| BPM 변화 | BMS `#BPMxx`와 channel 08 tempo event 파싱, 생성 시 tempo map 일부 생성 | `Chart/ChartGenerator.cs`, `Chart/NoteLane.cs` |
| 곡별 기록 | 곡 ID별 최고 점수, 최고 콤보, 최고 정확도, 난이도별 최고 점수 저장 | `Data/SongDataStore.cs` |
| 설정 저장 | 볼륨, 프레임레이트, VSync, 테마, 접근성, 오프셋 등 JSON 저장 | `Data/UserSettings.cs` |
| 업적 | 누적 플레이/점수/콤보/판정/미스리스 기반 업적과 토스트 | `Data/AchievementProgress.cs`, `Forms/GameForm_Achievement*.cs` |
| 접근성 | Form 접근성 이름/설명, 고대비, 색각 보정, reduced motion 옵션 일부 구현 | `Forms/GameForm.cs`, `Forms/GameForm_settings.cs` |

## 리듬게임 필수 완성 요소

### 1. 타이밍과 싱크//

- [v] 게임 시간 기준을 `Stopwatch delta`가 아니라 실제 오디오 재생 위치 기반으로 바꾸기
  - `AudioManager.GetInGameBgmPositionSeconds()`가 MCI `status position`을 읽고, `GameEngine.Update()`는 해당 값이 있을 때 playback position을 chart clock으로 사용한다.
  - 오디오 position을 읽을 수 없거나 BGM이 없을 때만 `Stopwatch delta` 누적을 fallback으로 사용한다.
- [v] 오디오 오프셋을 판정/노트 위치/카운트다운 시작 시점에 일관되게 적용하기
  - `AudioOffsetSeconds`는 playback position 기반 chart clock에서 한 번만 차감되어 노트 spawn, 위치, 판정이 같은 기준을 공유한다.
  - Settings와 결과 화면에 Early/Late bias와 평균 timing 표시를 추가했다.
- [v] 입력 지연 보정 화면 추가
  - Settings의 `CALIBRATE`에서 metronome tick 기반 입력 지연을 측정한다.
  - 평균 입력 지연은 `user_settings.json`의 `AudioOffsetMs`에 저장된다.
- [v] Early/Late 표시 추가
  - 판정 피드백에 `EARLY/LATE/SYNC`와 ms 차이를 함께 표시한다.
  - 결과 화면에는 Early/Late 개수와 평균 timing bias를 표시한다.
- [v] Pause 중 시간 정지 검증 강화
  - Pause 중에는 엔진 update가 멈추고 MCI BGM도 pause되므로 chart clock과 BGM position이 함께 정지한다.
  - Pause 진입/Resume 직후 모두 `_frameStopwatch.Restart()`를 호출해 fallback delta 튐도 줄였다.

### 2. 입력 시스템

- [v] 키 바인딩 설정 UI 추가
  - Settings의 `KEYS` 화면에서 4K/5K/7K 레인별 키를 직접 바꿀 수 있다.
  - 바인딩은 `user_settings.json`의 `KeyBindings4K/5K/7K`에 저장된다.
- [v] 키 중복/충돌 검사 추가
  - 같은 모드 안에서 중복 키를 막고 경고를 표시한다.
  - ESC, Enter, Back, Tab, Delete, modifier/system key는 막는다. Space는 5K/7K 중앙 레인에서만 허용한다.
- [v] 키보드 고스팅 안내 추가
  - `KEYS` 화면에 동시 입력 테스트 영역을 추가했다.
  - 여러 레인 키를 누를 때 highlight가 빠지는지 바로 확인할 수 있다.
- [v] 입력 로그/replay용 입력 이벤트 기록 추가
  - 플레이 중 `time, lane, input, keyDown/keyUp, judgment, source`를 기록한다.
  - 세션 종료 시 `%LOCALAPPDATA%/RhythmGame/input_logs`에 JSON으로 저장한다.
- [v] 마우스/터치 입력 여부 결정
  - 레인 클릭 입력을 지원한다.
  - 마우스 down/up은 키 입력과 같은 판정/hold/logging 경로를 사용한다.

### 3. 노트 타입과 판정 디테일

- [v] Long Note 완성도 강화
  - Long은 시작 판정과 끝 판정을 분리하고, 끝 release 또는 유지 완료 시 PERFECT/GREAT 등으로 별도 판정한다.
  - Hold tick 점수를 추가했고, 조기 release는 miss로 처리한다.
- [v] Slide Note 실제 조작 구현
  - Slide는 시작 lane hit 후 중간 지점부터 end lane을 요구한다.
  - end lane release 또는 end 지점까지 hold 유지로 끝 판정을 처리한다.
- [v] 동시치기 chord 처리 강화
  - 같은 시간대 note를 chord로 묶어 `CHORD xN L/R` 피드백을 표시한다.
  - chord note도 개별 판정은 유지하면서 묶음 정보와 손 배치 힌트를 같이 보여준다.
- [v] 노트 겹침 방지
  - chart 로드 후 lane별 최소 간격과 long/slide 점유 시간을 검사한다.
  - 같은 lane 또는 slide end lane에 겹치는 note는 로드 단계에서 제거한다.
- [v] Miss 판정 후 시각 효과 추가
  - Miss note는 즉시 사라지지 않고 짧은 시간 빨간 flash, X 표시, 흔들림으로 표시된다.
- [v] Hold 중 시각 피드백 추가
  - Hold 중 lane glow, note outline pulse, tail fill progress를 표시한다.

### 4. 판정, 점수, 랭크

- [v] Accuracy 계산식을 통일하기
  - 인게임 HUD, 결과 화면, 곡별 저장이 `ScoreManager.Accuracy`의 가중치 기반 accuracy를 공유한다.
  - PERFECT/GREAT/BETTER/GOOD/BAD/MISS 가중치를 공통 계산식으로 통일했다.
- [v] Grade/Rank 추가
  - S+, S, A, B, C, D, F를 산정한다.
  - Accuracy, miss count, max combo, clear type을 기준으로 결과 화면과 저장 기록에 반영한다.
- [v] Clear Type 추가
  - Failed, Clear, Full Combo, All Great+, Perfect 클리어 상태를 산정하고 곡별 기록에 저장한다.
- [v] 콤보 배율 점수의 폭주 여부 검토
  - `판정점수 * 현재콤보` 누적식을 제거하고 1,000,000점 만점 정규화 점수로 바꿨다.
  - Hold tick 보너스는 작은 상한을 둬 긴 곡에서도 점수가 폭주하지 않게 했다.
- [v] Max Miss Streak를 결과/업적에 적극 활용
  - 결과 화면에 Max Miss Streak를 표시한다.
  - 업적 진행도에는 max miss streak가 1 이하인 안정 클리어 조건을 반영한다.
- [v] 판정별 색상/사운드/애니메이션 차등화
  - PERFECT/GREAT/BETTER/GOOD/BAD별 feedback 색, 크기, 지속 시간을 다르게 둔다.
  - 기존 판정별 hit sound와 함께 BAD에는 짧은 shake를 적용한다.

### 5. 게이지와 실패 조건

- [v] Life Gauge 또는 Groove Gauge 추가
  - Good 이상은 회복, Bad/Miss는 감소한다.
  - Normal에서는 gauge가 0이 되면 즉시 Failed 처리하고, 곡 종료 시 clear threshold 미만이면 Failed 처리한다.
- [v] Practice Mode와 Normal Mode 분리
  - Settings의 PLAY MODE에서 Normal/Practice를 선택한다.
  - Practice는 gauge가 낮아도 실패 없이 끝까지 진행하고, Normal은 gauge 기반 clear/fail을 적용한다.
- [v] 난이도별 clear 조건 분리
  - Easy/Normal/Hard별 시작 gauge, clear threshold, 회복량, Bad/Miss 피해량을 다르게 적용한다.
  - Easy는 관대하고 Hard는 clear threshold와 피해량이 더 높다.
- [v] 위험 상태 UI 추가
  - 게이지가 낮을 때 Groove gauge, hit zone, 화면 테두리, 경고 문구가 빨간색으로 표시된다.

### 6. 채보 시스템

- [v] 채보 포맷 스키마 문서화
  - BMS subset 규칙을 `docs/chart-format.md`에 고정했다.
  - 지원 channel, token 의미, long/slide 표현, tempo event 규칙을 문서화했다.
- [v] BMS 파서 검증 강화
  - 잘못된 token, 너무 큰 measure, 음수/0 BPM, 비정상 resolution을 warning으로 수집한다.
  - undefined tempo token, 현재 lane mode 밖의 channel도 진단한다.
- [v] 생성 채보 검증기 추가
  - 최소 간격, 같은 레인 반복/겹침, 동시치기 개수, long/slide 점유, slide 도착 lane 유효성을 검사한다.
  - 로드/저장/프리뷰/에디터가 `ChartValidator`의 같은 검증 결과를 사용한다.
- [v] 수동 Chart Editor를 앱 내부 UI로 구현
  - Song Select에서 `E`를 누르면 앱 내부 Chart Editor가 열린다.
  - grid, 재생 위치, note 추가/삭제, lane 이동, BPM 조정, 저장, 되돌리기, 짧은 preview 재생을 지원한다.
- [v] 채보 preview 재생
  - Song Select에서 선택 곡을 15초 미리듣기로 재생한다.
  - 선택한 난이도/레인 모드의 density preview와 chart warning 수를 표시한다.
- [v] 곡/난이도/레인 모드별 chart 분리
  - `easy_song_4k.bms`, `normal_song_7k.bms` 같은 lane-specific chart를 우선 로드한다.
  - 기존 난이도별 chart는 legacy fallback으로 유지한다.
- [v] 난이도 수치화
  - Easy/Normal/Hard 외에 Lv.1~Lv.15를 산출한다.
  - note density, chord 비율, jack 비율, long/slide 비율, hand movement로 계산한다.

### 7. 자동 채보 생성 품질

- [v] MP3/OGG/FLAC 분석 지원
  - WAV는 내장 PCM 분석기를 사용하고, MP3/OGG/FLAC은 `ffmpeg`가 있으면 임시 PCM WAV로 디코드해 분석한다.
  - 디코더가 없으면 Song Select에 `analysis needs ffmpeg`로 표시하고 자동 채보 생성을 건너뛴다.
- [v] beat detection 고도화
  - RMS + transient에 low/mid/high band onset, spectral flux, adaptive threshold, confidence를 추가했다.
  - kick 계열 low energy와 high transient를 분리해 패턴 선택에도 사용한다.
- [v] Downbeat/마디 추정
  - BPM 기준 후보 offset을 훑어 low energy와 confidence가 강한 measure start를 추정한다.
  - 생성 노트의 measure/offset quantize에 downbeat offset을 반영한다.
- [v] BPM 변속 정확도 개선
  - tempo segment에 confidence를 추가하고 최소 segment 길이와 BPM 변화 폭 필터를 적용한다.
  - Chart Editor의 BPM 조정/저장 경로로 기본 BPM 수동 보정이 가능하다.
- [v] 패턴 문법 추가
  - Stream, Stair, Trill, Jack, Chord, Roll, Long hold, Slide, Rest 패턴을 구간 분석 결과로 선택한다.
- [v] 손 배치 난이도 제어
  - 같은 손 연속, 가까운 jack, 5K/7K 중앙 lane 남발을 lane assignment에서 완화한다.
- [v] 구간별 density curve
  - 8~12초 단위 energy section을 만들고 low/rest, normal, high/drop 밀도 배율을 적용한다.
- [v] 유저 실력 기반 동적 난이도 정교화
  - `song_data.json`에 곡별/난이도별/레인모드별 best accuracy와 adaptive density를 저장한다.
  - 생성 chart 로드시 해당 mode key를 우선 사용해 note density를 낮춘다.

### 8. 오디오 시스템

- [v] 오디오 엔진 교체 검토
  - `AudioEngineCatalog`에 MCI/NAudio/BASS/CSCore 후보와 현재 MCI 한계를 코드로 고정했다.
  - 현재는 무의존 MCI를 유지하되, 정밀 position/codec/mixing이 필요하면 NAudio가 우선 교체 후보이다.
- [v] BGM/SFX 동시 재생 정책 정리
  - BGM/Preview는 MCI alias, hit sound는 `PlaySound` 메모리 WAV로 분리해 동시에 재생한다.
  - hit sound mute는 별도 설정으로 분리하고 BGM mute 경로와 섞지 않는다.
- [v] hit sound 스킨화
  - `Songs/HitSounds/{Skin}/perfect.wav` 같은 judgment별 WAV asset을 로드한다.
  - Settings에서 skin, pitch, mute, SFX volume을 조정하고 asset이 없으면 synth tone으로 fallback한다.
- [v] preview volume과 game volume 분리
  - Settings에 preview volume을 추가하고 Song Select/Chart Editor 미리듣기에 별도 볼륨을 사용한다.
- [v] 음악 종료 이벤트 감지
  - MCI position/length/status로 BGM 종료를 감지한다.
  - chart 완료 후에는 BGM 종료 또는 짧은 chart-complete grace 기준으로 결과 화면에 진입한다.

### 9. 곡 라이브러리와 메타데이터

- [v] sidecar JSON 스키마 고정
  - `docs/song-sidecar-schema.md`에 `title`, `artist`, `bpm`, `durationSeconds`, `previewStart`, `previewEnd`, `genre`, `source`, `bga`, `cover`를 고정했다.
  - 런타임 metadata도 같은 필드를 읽어 Song Select/preview/detail에 반영한다.
- [v] 곡 ID 충돌 처리
  - 파일명 정규화 ID에 전체 경로 hash suffix를 섞어 같은 이름/다른 확장자 충돌을 막는다.
  - 기존 filename-only score는 discovery 시 fallback으로 읽는다.
- [v] 앨범아트 지원
  - sidecar의 `cover` 상대/절대 경로를 읽어 Song Select와 상세 화면 artwork로 표시한다.
  - cover가 없거나 로드 실패 시 기존 style 기반 artwork로 fallback한다.
- [v] 정렬/필터 추가
  - Song Select에 제목, 아티스트, BPM, 길이, 최고점수, 최근 플레이, 난이도, 즐겨찾기 정렬을 추가했다.
  - 즐겨찾기 필터와 `F` 단축키를 지원한다.
- [v] 곡 rescan 기능
  - Song Select의 `RESCAN` 버튼과 `R` 단축키로 캐시를 무효화하고 곡 목록/preview를 다시 읽는다.
- [v] 곡별 상세 기록 화면
  - Song Select의 `DETAIL` 버튼 또는 `D` 단축키로 곡 상세 화면을 연다.
  - 난이도별 Lv, 최고점수, accuracy, clear type, play count, 최근 플레이 시간을 표시한다.

### 10. UI / UX

- [v] 메인 화면 정보 밀도 조정
  - 메인 화면은 큰 패널 없이 유지하고, 하단에 곡 수, 최고점수, 현재 lane mode만 작은 summary로 표시한다.
  - 상세 best score와 곡 정보는 Song Select/Detail 화면 안에서만 다룬다.
- [v] 인게임 HUD 정리
  - Score, Accuracy, Groove, Combo, 판정 카운트는 play area 내부 고정 레이아웃으로 정리했다.
  - Speed, Mode, Lane, Play Mode는 상단 HUD chip으로 통합해 4K/5K/7K에서 사이드 영역과 겹치지 않게 했다.
- [v] Pause Overlay 고도화
  - Pause 중 Resume, Retry, Song Select, Settings Locked, Exit 버튼을 명확히 표시한다.
  - 설정 변경은 pause 중 잠금으로 결정했고, resume/retry/song select/exit는 마우스 버튼으로 실행된다.
- [v] 결과 화면의 리듬게임 정보 강화
  - Grade, Clear Type, Early/Late 평균, max miss streak 표시를 유지하고 판정 분포 그래프를 추가했다.
  - 이전 최고점수를 넘긴 경우 `NEW RECORD` badge를 표시한다.
- [v] Song Select 조작 안내 정리
  - 화면 하단에 `Enter`, `D`, `E`, `F`, `R`, `5/6`, `Esc` key hint를 추가했다.
  - 숨겨져 있던 chart edit, favorite, rescan, lane mode 조작을 Song Select에서 바로 확인할 수 있다.
- [v] Settings에 Lane Mode 노출
  - Settings 하단에 4K/5K/7K segmented control을 추가했다.
  - 같은 화면의 `KEYS` 버튼과 연결되어 lane mode와 키 바인딩을 한 곳에서 관리한다.
- [v] 반응형 레이아웃 보완
  - play area 폭 계산에 lane count별 최소 lane width와 safe margin을 적용했다.
  - Pause 버튼 폭도 화면 폭에 맞춰 줄어들게 해 좁은 해상도에서 버튼이 화면 밖으로 나가지 않게 했다.
- [v] 텍스트 인코딩 정리
  - 사용자에게 보이는 game mode 라벨을 `NORMAL/BLIND/FOG` ASCII 문자열로 통일했다.
  - 새로 추가한 UI 문구와 버튼 라벨은 UTF-8/ASCII 기준으로 정리했다.

### 11. 접근성

- [v] Screen Reader용 컨트롤 정보 확장
  - custom drawing UI 위에 virtual accessible node 목록을 추가해 버튼, 탭, 슬라이더, 리스트 항목을 screen reader child control로 노출한다.
  - 각 node는 name, description, role, bounds, default action을 가진다.
- [v] 키보드 전용 UI 네비게이션 완성
  - Tab/Shift+Tab 또는 Up/Down으로 화면 내 focus node를 이동하고 Enter/Space로 실행한다.
  - Settings slider/segmented control은 Left/Right로 조정하며, Main/Song Select/Achievement/Result/Settings/Calibration/Key Binding/Chart Editor/Pause overlay에 적용했다.
- [v] 고대비 테마 검증
  - Song Select, Achievement, Analyze, Settings 계열 panel/row/tab/border/text 색상에 `UseHighContrast` 분기를 추가했다.
  - focus ring은 고대비에서도 보이도록 accent 색과 dashed outline을 사용한다.
- [v] 색각 보정의 실제 적용 범위 확대
  - lane/note 색상은 color vision palette를 사용하고 high contrast에서는 흰색/노란색 중심으로 바뀐다.
  - judgment feedback과 결과 화면 판정 분포 그래프도 color vision palette를 사용한다.
- [v] Reduced Motion 적용 범위 확대
  - Splash wave/particle, Bad feedback shake, gauge danger pulse에 이어 achievement toast slide와 주요 hover lift도 Reduced Motion에서 멈춘다.
- [v] 글자 크기 옵션
  - Settings에 `TEXT SIZE` slider를 추가했다.
  - 결과 화면, Settings, Song Select 주요 텍스트는 저장된 text scale을 반영한다.

### 12. 진행도, 기록, 리플레이

- [v] 곡별 기록을 난이도 + 레인모드 단위로 저장
  - 현재 난이도별 최고점수는 있으나 레인모드별 분리가 없다.
  - 4K Normal과 7K Normal은 별도 기록이어야 한다.
- [v] 기록 history 저장
  - 최고 기록만 저장하면 성장 추이를 볼 수 없다.
  - 최근 N회 점수, accuracy, miss, clear type, 날짜를 저장한다.
- [v] Replay 저장/재생
  - chart version, song ID, input events, offset, speed, lane mode를 함께 저장한다.
- [v] 업적 문구 인코딩 복구
  - 업적 정의 문자열이 깨져 보이는 상태다.
  - 실제 UI에 노출되는 한국어 문구는 전부 재작성해야 한다.
- [v] 업적 조건을 곡별/난이도별로 확장
  - 현재 대부분 누적형이다.
  - Hard clear, 7K clear, Full Combo, 특정 BPM 이상 곡 clear 같은 목표가 있으면 플레이 동기가 높아진다.

### 13. 스킨과 시각 효과

- [v] 노트 스킨 시스템
  - `docs/visual-skin-format.md` 규칙에 따라 note body, long tail, slide arrow, hit burst, miss effect 이미지를 교체할 수 있다.
- [v] Lane skin과 hit zone skin
  - `skin.json`의 lane color, separator, key 색상, pressed/hold tint, hit line/glow 색상을 적용한다.
- [v] 배경/BGA 지원
  - sidecar의 `bga` 이미지가 있으면 인게임 배경으로 표시하고, 없으면 audio position 기반 reactive background를 표시한다.
- [v] Combo milestone 연출
  - 50/100/200 combo와 이후 100 단위 combo 달성 시 작은 milestone badge를 표시한다.
- [v] 판정 feedback 위치 조정
  - 판정 feedback을 중앙에서 hit zone 우측 근처로 옮겨 note 시야를 덜 가리게 했다.

### 14. 성능과 안정성

- [v] 프레임 중 allocation 점검
  - 인게임 draw frame의 allocation sample을 `%LOCALAPPDATA%/RhythmGame/logs`에 남긴다.
  - 인게임 HUD/feedback/lane label 중심 Font/Brush/Pen을 `RenderResourceCache`로 재사용한다.
- [v] GDI resource 누수 검사
  - 게임 시작/진행/종료 시 GDI object count를 샘플링해 증가량을 로그에 남긴다.
- [v] 대량 곡 라이브러리 성능
  - Song Select discovery에서 metadata를 곡마다 저장하지 않고 batch upsert로 한 번만 저장한다.
  - 기존 song list cache와 결합해 수백 곡 탐색 시 저장 I/O를 줄였다.
- [v] chart generation 비동기화
  - 앱 시작 시 `ChartGenerator.BeginGenerateAllChartsAsync()`로 백그라운드 생성한다.
  - 메인 화면 summary에 chart generation 진행 상태를 표시한다.
- [v] 예외 로깅
  - 주요 저장/분석/metadata/skin/BGA 실패를 `%LOCALAPPDATA%/RhythmGame/logs`에 기록한다.
- [v] 저장 파일 백업/복구
  - `player_progress.json`, `song_data.json`, `user_settings.json` 저장 시 `.bak`를 만들고 읽기/파싱 실패 시 복구를 시도한다.

### 15. 테스트와 검증

- [v] 단위 테스트 추가
  - `Tests/MuWorld.SelfTests.csproj`에서 `ScoreManager`, `NoteLane` BMS parse, `ChartGenerator` filename/tempo, `UserSettingsStore`, `SongDataStore`를 검증한다.
- [v] 판정 시뮬레이션 테스트
  - target time 대비 입력 시간으로 PERFECT/GREAT/BETTER/GOOD/BAD/MISS가 정확히 나오는지 검증한다.
- [v] long/slide 테스트
  - hold 시작, 중간 release, end timing, wrong lane 입력을 케이스로 검증한다.
- [v] 곡별 기록 저장 테스트
  - 동일 곡/난이도/레인모드에서 최고 기록이 분리 저장되고 낮은 점수로 덮이지 않는지 검증한다.
- [v] UI smoke test
  - Splash -> Main -> Song Select -> Game -> Analyze paint 경로를 bitmap 렌더링으로 자동 검증한다.
- [v] 해상도 테스트
  - 960x640, 1152x768, 1366x768, 1920x1080, 2560x1080에서 주요 화면이 예외 없이 nonblank로 렌더링되는지 검증한다.
- [v] 긴 플레이 테스트
  - 10분 분량 engine simulation으로 note cleanup, judged note count, 실행 시간 예산을 검증한다.

## 우선순위 제안

### P0: 리듬게임으로 바로 체감되는 핵심

1. 오디오 position 기반 chart clock 전환
2. Accuracy 계산식 통일 및 Grade/Clear Type 추가
3. Long Note 끝 판정과 Slide 조작 완성
4. Settings에 Lane Mode/Key Binding UI 추가
5. 결과 화면에 기록 갱신, grade, clear type 표시

### P1: 콘텐츠 생산성과 반복 플레이

1. 앱 내부 Chart Editor
2. chart validation
3. 곡별/난이도별/레인모드별 기록 분리
4. replay 저장
5. song metadata/cover/preview 지원

### P2: 완성도와 확장성

1. 오디오 엔진 교체 또는 MCI position 안정화
2. 자동 채보 생성 품질 개선
3. 스킨/BGA 시스템
4. 접근성 세부 구현
5. 테스트 자동화와 로그 시스템

## 현재 특히 주의할 점

- 실제 플레이 싱크는 리듬게임의 가장 중요한 품질이다. 현재는 MCI position 기반 clock을 우선 사용하지만, MCI 자체 latency/codec 한계는 장기적으로 전용 오디오 엔진 교체 후보로 남아 있다.
- 4K/5K/7K와 Tap/Long/Slide 모델은 이미 들어와 있지만, UI와 판정 세부 규칙은 아직 완성도가 다르다. "모델 존재"와 "플레이 경험 완성"을 분리해서 봐야 한다.
- 곡별 기록과 설정 저장은 구현되어 있으나, 레인모드 단위 기록과 key binding 저장은 더 필요하다.
- README와 일부 주석/문자열의 인코딩이 깨져 보인다. 사용자에게 보이는 문구부터 우선 정리하는 것이 좋다.
