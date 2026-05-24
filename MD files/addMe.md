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

## P0: 리듬게임으로서 즉시 고쳐야 할 항목

### 1. 6K 키 바인딩 저장 스키마 분리

문제:

- 코드에는 6K 모드가 있지만 `UserSettings`에는 `KeyBindings6K`가 없다.
- 현재 구조에서는 6K 사용자 설정이 명확하게 영속화되지 않는다.

수정 요구:

- `Data/UserSettings.cs`에 `KeyBindings6K`를 추가한다.
- 기존 설정 파일을 깨지 않도록 migration fallback을 둔다.
- `Forms/GameForm_keybindings.cs`의 serialize/load/reset 흐름에서 6K를 별도 저장한다.
- Settings UI에서 4K/5K/6K/7K 전체가 저장, 재실행, 재로드 후 유지되어야 한다.

완료 조건:

- 6K 키를 바꾸고 앱을 재시작해도 유지된다.
- 4K/5K/7K 설정이 회귀하지 않는다.
- self-test에 6K 저장/복구 케이스가 추가된다.

### 2. 오디오 엔진 안정성 검증

문제:

- MCI는 간단하지만 codec, position precision, device state, alias 관리에서 한계가 있다.
- 리듬게임은 음악 위치와 판정 타이밍이 핵심이므로 오디오 clock 신뢰도를 엄격히 봐야 한다.

수정 요구:

- MCI position jitter를 측정해 로그로 남긴다.
- pause/resume, seek, preview 전환, chart complete에서 position이 튀는지 검증한다.
- NAudio 등 대체 엔진 도입 여부를 실험하고, 실제로 더 안정적인 경우에만 교체한다.

완료 조건:

- 같은 곡을 5회 이상 반복 플레이해도 결과 timing bias가 비정상적으로 drift하지 않는다.
- pause/resume 후 첫 2초 판정이 안정적이다.
- 비-WAV 재생과 WAV 재생의 position handling 차이를 문서화한다.

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

문제:

- 메인 메뉴 진입점이 변경되면서 Settings, Player, Quit의 시각적 역할이 계속 조정되고 있다.
- 사용자가 처음 봤을 때 무엇을 누르면 어디로 가는지 즉시 알아야 한다.

수정 요구:

- Main Menu의 모든 클릭 가능 영역에 hover 피드백을 둔다.
- Settings는 항상 보이는 진입점이 있어야 한다.
- Player 영역이 Statistics로 이동한다는 것을 직관적으로 보여야 한다.
- Quit은 위치와 형태가 다른 메뉴와 충돌하지 않아야 한다.

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

### 6. 결과 화면의 학습 피드백 강화

문제:

- 점수와 판정 수치만으로는 사용자가 무엇을 고쳐야 하는지 알기 어렵다.

추가 요구:

- Early/Late bias를 기반으로 "조금 빠름", "조금 늦음", "안정적" 같은 요약을 제공한다.
- miss가 몰린 구간을 timeline으로 표시한다.
- Long/Slide 실패 원인을 시작 실패, 유지 실패, 종료 실패로 분리한다.

완료 조건:

- 결과 화면에서 다음 플레이 목표가 명확히 보인다.
- ScoreManager와 engine event가 같은 원인을 기록한다.

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

문제:

- 리플레이는 입력 이벤트만 저장해서는 chart 변경, offset 변경, speed 변경에 취약할 수 있다.

추가 요구:

- replay에 chart hash, game version, settings snapshot을 포함한다.
- chart가 바뀐 경우 재생 전 경고한다.
- replay 결과와 원래 결과가 다르면 mismatch를 표시한다.

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

- `docs/visual-skin-format.md`와 실제 로더가 일치한다.
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
.\dotnet\dotnet.exe build .\Tests\MuWorld.SelfTests.csproj -c Debug --no-restore -v:minimal
.\dotnet\dotnet.exe .\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll
```

Release 검증은 `game start.exe`가 실행 중이면 파일 잠금으로 실패할 수 있다. Release 검증 전에는 실행 중인 Release 앱을 종료한다.

완료 기준:

- 빌드 경고 0개
- self-test 실패 0개
- 변경한 UI를 최소 1366x768과 1920x1080에서 직접 확인
- 새 저장 데이터가 기존 저장 파일을 깨지 않음
- 문서가 실제 코드와 불일치하지 않음
