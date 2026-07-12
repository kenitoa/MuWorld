# MuWorld 추가/수정 요구사항

이 문서는 MuWorld를 잘 만들어진 리듬게임 수준으로 끌어올리기 위해 반드시 확인해야 할 항목을 매우 엄격하게 정리한 백로그입니다. 여기에 있는 항목은 단순 아이디어가 아니라, 구현 후 테스트와 실제 플레이 검증까지 통과해야 완료로 볼 수 있습니다.

## 운영 원칙

- 완료 표시는 실행 가능한 검증이 통과한 뒤에만 한다.
- README나 코드 주석에만 적힌 기능은 완료가 아니다.
- UI가 그려져도 실제 플레이 흐름에서 쓸 수 없으면 완료가 아니다.
- 한 해상도에서만 맞는 UI는 완료가 아니다.
- 한 곡에서만 맞는 판정/차트/오디오는 완료가 아니다.
- 자동 테스트가 통과해도 실제 플레이 감각이 나쁘면 완료가 아니다.
- 새 기능은 `ReadMe.md`, `Tech.md`, 이 문서 중 필요한 위치에 반영한다.
- 검증이 끝난 항목은 이 백로그에서 제거하고 실제 동작과 구조를 `ReadMe.md` 또는 `Tech.md`에 반영한다.

## P0: 리듬게임으로서 즉시 고쳐야 할 항목

### 2. 오디오 엔진 안정성 검증

문제:

- MCI는 간단하지만 codec, position precision, device state, alias 관리에서 한계가 있다.
- 리듬게임은 음악 위치와 판정 타이밍이 핵심이므로 오디오 clock 신뢰도를 엄격히 봐야 한다.
- format별 jitter/drift/역행/stall telemetry와 인게임 BGM MCI 오류 로그, 판정 clock 역행 방어, pause 후 hold 재입력 grace는 구현됐지만 Main/preview 전체 오류 계측과 실제 장치 반복 측정은 아직 완료되지 않았다.

수정 요구:

- `%LOCALAPPDATA%/RhythmGame/logs`의 audio clock summary를 같은 WAV와 비-WAV 곡에서 각각 5회 이상 수집한다.
- pause/resume, preview 전환, chart complete에서 position과 첫 2초 판정이 튀는지 실제 장치에서 검증한다.
- NAudio 등 대체 엔진 도입 여부를 실험하고, 실제로 더 안정적인 경우에만 교체한다.

완료 조건:

- 같은 곡을 5회 이상 반복 플레이해도 결과 timing bias가 비정상적으로 drift하지 않는다.
- pause/resume 후 첫 2초 판정이 안정적이다.
- 비-WAV 재생과 WAV 재생의 측정 결과를 수치와 함께 문서화한다.

### 3. 실제 플레이 감각 테스트 세트 구축

문제:

- 현재 self-test는 로직과 렌더링 smoke에 강하지만, 사람 기준의 플레이 감각을 완전히 보장하지 않는다.

수정 요구:

- 짧은 튜토리얼 곡, 중간 난이도 곡, 고밀도 곡을 기준 테스트 세트로 고정한다.
- 각 곡에 수동 검증 체크리스트를 만든다.
- note density, chord, long, slide, BPM 변화, rest 구간을 모두 포함한다.

완료 조건:

- 각 기준 곡에 대해 플레이 영상 또는 로그 기반 검증 결과가 남는다.
- 판정선, note speed, hit sound, feedback, 결과 화면까지 끊김 없이 확인된다.

### 4. 메인 메뉴와 화면 이동 UX 정리

현재 상태:

- 모든 메뉴 진입점의 hover와 keyboard/accessibility node는 구현돼 있다.
- Player 표시는 `PLAYER STATS`와 실제 play count로 바뀌었고 Statistics 목적지를 명확히 한다.
- 남은 핵심은 실제 screen reader 점검과 해상도별 텍스트/클릭 영역 겹침 검증이다.

수정 요구:

- 실제 screen reader에서 시각 순서, 이름, 설명, 실행 동작을 점검한다.
- Settings, Player Stats, Quit의 label과 hit target을 세 기준 해상도에서 자동 overlap 검사한다.

완료 조건:

- 마우스 없이 keyboard navigation으로 Play, Settings, Player, Restart, Quit에 접근 가능하다.
- 1366x768, 1920x1080, 2560x1080에서 텍스트와 클릭 영역이 겹치지 않는다.
- screen reader node와 실제 화면 요소가 불일치하지 않는다.

## P1: 완성도를 높이기 위해 추가해야 할 항목

### 5. 튜토리얼과 첫 플레이 안내

문제:

- 처음 접하는 사용자는 4K/5K/6K/7K, Tap/Long/Slide, Early/Late 의미를 알기 어렵다.

추가 요구:

- 첫 실행 시 선택 가능한 tutorial chart를 제공한다.
- 판정선과 입력 키를 단계적으로 알려준다.
- Long과 Slide를 별도 구간에서 연습하게 한다.
- CALIBRATE를 자연스럽게 안내한다.

완료 조건:

- 신규 사용자가 README를 읽지 않아도 첫 곡을 시작하고 끝낼 수 있다.
- 튜토리얼 종료 후 기본 설정이나 Song Select로 자연스럽게 이어진다.

### 7. 차트 에디터 사용성 강화

문제:

- 차트 에디터가 존재해도 생산성이 낮으면 실제 콘텐츠 제작에 쓰기 어렵다.

추가 요구:

- grid snapping 강도를 선택할 수 있어야 한다.
- 복사/붙여넣기, 구간 선택, mirror, shift, quantize 기능을 추가한다.
- chart validation warning을 에디터에서 바로 위치로 이동할 수 있게 한다.
- 저장 전 난이도 추정과 density curve를 보여준다.

완료 조건:

- 1분짜리 간단한 곡을 에디터에서 처음부터 끝까지 만들 수 있다.
- 저장한 chart가 즉시 Song Select와 플레이에 반영된다.

### 8. 곡 라이브러리 관리 개선

문제:

- 곡이 많아지면 단순 파일 탐색만으로는 관리가 어렵다.

추가 요구:

- genre, source, favorite, recently played, level range 필터를 강화한다.
- 누락된 sidecar, 누락된 chart, 분석 실패 사유를 목록에서 볼 수 있게 한다.
- cover/BGA 로딩 실패를 사용자에게 조용히 숨기지 말고 상태로 보여준다.

완료 조건:

- 곡 100개 이상에서도 목록 탐색이 지연 없이 동작한다.
- 잘못된 곡 파일이 있어도 전체 라이브러리가 망가지지 않는다.

### 9. 리플레이 신뢰성 강화

현재 상태:

- replay v3는 실제 플레이 차트 snapshot과 hash, audio SHA-256, game assembly version, offset/speed/play mode snapshot, 최대 콤보/연속 MISS와 노트별 의미 판정 결과를 저장한다.
- 재생 전 format/version/song/mode/chart/input 검증과 재생 후 결과 비교가 구현돼 있으며 불일치는 화면과 로그에 표시된다.
- 남은 핵심은 실제 오디오 재생을 포함한 반복 결정론 검증이다.

추가 요구:

- 같은 v3 replay를 실제 곡에서 5회 이상 반복 재생하는 end-to-end 검증을 추가한다.
- 구형 replay를 hard block할지 별도 변환 도구로 옮길지 migration 정책을 확정한다.

완료 조건:

- 같은 replay를 반복 재생했을 때 같은 score와 판정 분포가 나온다.
- chart/version mismatch가 조용히 실패하지 않는다.

## P2: 장기 품질과 확장성

### 10. 스킨과 BGA 시스템 정리

추가 요구:

- visual skin package 구조를 안정화한다.
- 잘못된 skin asset이 있어도 기본 스킨으로 안전하게 fallback한다.
- BGA 이미지/영상 지원 범위를 명확히 정한다.

완료 조건:

- `front interface/docs/visual-skin-format.md`와 실제 로더가 일치한다.
- 스킨 오류가 게임 실행 실패로 이어지지 않는다.

### 11. 성능 기준 수립

추가 요구:

- 인게임 FPS, frame time, allocation, GDI object count를 기준화한다.
- 저사양 모드와 high quality 모드의 차이를 명확히 한다.
- 10분 이상 플레이 후 메모리와 GDI object가 계속 증가하지 않아야 한다.

완료 조건:

- self-test 또는 별도 soak test에서 성능 수치가 기록된다.
- 회귀 기준을 넘어가면 실패하도록 한다.

### 12. 접근성 완성도 강화

추가 요구:

- 모든 custom-drawn 버튼에 accessible node가 있어야 한다.
- focus order가 화면 배치와 일치해야 한다.
- 고대비, 색각 보정, reduced motion, text size가 모든 주요 화면에 적용되어야 한다.

완료 조건:

- Main, Song Select, Settings, Statistics, Analyze, Chart Editor가 키보드만으로 사용 가능하다.
- screen reader 이름과 실제 화면 텍스트가 서로 어긋나지 않는다.

### 13. 문서와 코드 상태 동기화

추가 요구:

- 사용자 설명은 `ReadMe.md`에만 둔다.
- 내부 구현과 파일 구조는 `Tech.md`에만 둔다.
- 미완성/개선/추가 작업은 `addMe.md`에만 둔다.
- 구현 상태가 바뀌면 세 문서 중 해당 문서를 반드시 갱신한다.

완료 조건:

- README성 문서가 계획표로 오염되지 않는다.
- addMe 항목은 완료 검증 없이 체크 처리하지 않는다.

## 검증 게이트

새 기능이나 수정은 최소한 다음 검증을 통과해야 한다.

```powershell
& ".\front interface\dotnet\dotnet.exe" build ".\front interface\Tests\MuWorld.SelfTests.csproj" -c Debug --no-restore -v:minimal
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll"
```

Release 검증은 `game start.exe`가 실행 중이면 파일 잠금으로 실패할 수 있다. Release 검증 전에는 실행 중인 Release 앱을 종료한다.

완료 기준:

- 빌드 경고 0개
- self-test 실패 0개
- 변경한 UI를 최소 1366x768과 1920x1080에서 직접 확인
- 새 저장 데이터가 기존 저장 파일을 깨지 않음
- 문서가 실제 코드와 불일치하지 않음
