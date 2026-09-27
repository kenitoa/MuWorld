# MuWorld 품질 검증

이 문서는 검증 방법과 관찰 결과를 다룹니다. 사용자 기능은 루트 README, 미완료 작업은 `MD files/addMe.md`를 기준으로 합니다. 시뮬레이션, 실제 오디오 장치 재생, 사람의 플레이 평가는 서로 다른 증거입니다.

## 기본 게이트

저장소 루트에서 실행합니다. 로컬 SDK와 restore 결과가 있어야 `--no-restore`를 사용할 수 있습니다.

```powershell
& ".\front interface\dotnet\dotnet.exe" build ".\front interface\Tests\MuWorld.SelfTests.csproj" -c Debug --no-restore -v:minimal
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll"
& ".\front interface\dotnet\dotnet.exe" build ".\front interface\Tests\MuWorld.SelfTests.csproj" -c Release --no-restore -v:minimal
git diff --check
```

C# 컴파일러의 타입 검사와 빌드 경고를 함께 확인합니다. 별도의 npm lint/typecheck는 이 WinForms 프로젝트에 해당하지 않습니다. 기존 앱을 실행 중이면 Release 파일 잠금이 발생할 수 있으므로 변경 배포 전 종료합니다.

스크린샷을 저장하려면 테스트 실행 전에 `MUWORLD_CAPTURE_DIR`을 지정합니다. 이는 테스트 전용 선택 환경 변수이며 운영 필수 설정은 아닙니다.

## 반복 오디오·리플레이 검증

```powershell
# 고정 9초 PCM/차트/입력을 생성하여 실제 MCI 출력 장치에서 5회 재생
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll" --audio-repeat

# 사용자 v3 리플레이와 실제 곡을 지정하는 경우
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll" --audio-repeat "C:\Music\song.wav" "C:\Replays\song.json"
```

시작 전 format/version/song/chart/audio hash를 검증합니다. 4ms/17ms 대기 간격을 번갈아 사용하고 각 회차 초반에 pause/resume을 수행합니다. 기본 fixture는 동시치기, Long, Slide를 포함합니다. 실제 MCI 출력 경로를 사용하되 볼륨은 0이므로 청취 평가가 아닙니다.

비교 항목은 점수, 정확도, 판정 분포, 콤보, 노트별 의미 판정입니다. 등급/클리어 문자열은 참조 결과를 전달하므로 이 harness만으로 게이지·결과 화면의 종단간 재현성을 주장하지 않습니다. 원본 기록과 업적은 테스트 전용 저장 경로로 격리합니다. 출력 위치는 콘솔의 `Quality artifacts`에 표시합니다.

WAV와 같은 원본의 MP3/OGG/FLAC로 각각 반복해야 합니다. 압축 형식은 인코딩 지연을 확인하고 해당 파일용으로 기록한 리플레이를 사용합니다. 확장자만 바꾸거나 audio hash를 조작해서 기존 리플레이를 재사용하지 않습니다.

## 장기 렌더링 검증

```powershell
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll" --soak 600
```

1366×768에서 실제 시간 600초 동안 GameEngine과 게임 렌더러를 실행합니다. 30초마다 draw p95/p99, private memory, GDI count를 기록합니다. 첫 30초 이후 GDI +64, private memory +128MiB 또는 누적 draw p99 50ms 초과 시 실패합니다. 이는 초기 회귀 예산이며 목표 하드웨어의 최종 성능 보장은 아닙니다.

이 테스트는 실제 오디오, 사람 입력, 화면 전환을 포함하지 않습니다. 별도로 10분 이상 곡 선택 → preview → 플레이 → 결과 → 재시작을 반복해 확인해야 합니다. 인게임 `Frame summary`는 프레임 간격, soak는 그리기 실행 시간이라는 차이도 유지합니다.

## 기준 콘텐츠와 수동 체크리스트

```powershell
& ".\front interface\dotnet\dotnet.exe" ".\front interface\Tests\bin\Debug\net9.0-windows\MuWorld.SelfTests.dll" --export-fixtures
```

출력은 각각 60초인 Tutorial/Medium/Dense PCM 박자 트랙, 4K BMS 차트, sidecar와 SHA-256 manifest입니다. 실제 음악의 예술적 완성도를 평가하는 곡이 아니라 재현용 기술 패턴입니다. export는 기존 라이브러리를 변경하지 않습니다. 수동 검증 시 WAV/sidecar를 테스트용 Songs 폴더에, `Charts` 안의 BMS를 테스트 프로필의 `%LOCALAPPDATA%/RhythmGame/Charts`에 배치하고 해당 난이도를 선택합니다. 기존 파일을 덮어쓰지 않습니다.

| 구간 | 확인 사항 |
|---|---|
| 0~20초 | 난이도별 밀도, 키 입력 누락, 판정선, note speed, 타격음 |
| 20~24초 | 쉼 구간에서 불필요한 노트/판정이 없는지 |
| 24~26초 | Long 중 pause/resume, 재입력 grace, 종료 판정 |
| 28~30초 | Slide 시작/전환/끝 입력과 피드백 |
| 32초 | 120→150 BPM 변화와 grid 정렬 |
| 34~58초 | 동시치기, 고밀도 프레임, 결과 집계 |

각 실행에 빌드, 장치/드라이버, 화면 크기/DPI, 차트·오디오 해시, 설정, 로그/영상, 관찰 결과를 남깁니다. 자동 생성·적응형 밀도가 기준 차트를 바꾸지 않도록 사용자 차트를 사용합니다.

튜토리얼은 README 없이 시작/완료가 가능한지 신규 사용자로 확인합니다. 에디터는 1분 차트 작성 → 저장 → 재열기 → Song Select 실행을 확인합니다. 접근성은 Main, Song Select, Settings, Statistics, Analyze, Chart Editor에서 키보드·스크린리더로 별도 검사합니다. 해상도는 1366×768, 1920×1080, 2560×1080과 Windows 125%/150% 배율을 구분합니다.

## 2026-09-27 실행 결과

- 자동 self-test: Debug 및 최종 Release에서 각각 37개 통과. 저장 round-trip, 원본 보존, BPM Undo/Redo, authored chart 불변성, tempo 보존, 스냅, 선택 편집, 120개 합성 곡 필터/cache, 튜토리얼 패턴, 프레임 histogram을 포함합니다.
- Debug/Release 빌드: 경고 0, 오류 0. 중간 Release 테스트 한 번은 Windows 애플리케이션 제어가 DLL을 차단했습니다(`0x800711C7`). 보안 정책은 변경하지 않았으며 최종 Release 실행에서는 재현되지 않고 37개 테스트가 통과했습니다.
- 실제 장치 WAV 반복: 5/5 `REPLAY VERIFIED`, 모두 1,000,000점. 상세 clock와 의미 판정은 [audio-repeat.json](validation/2026-09-27/audio-repeat.json)에 있습니다. clock의 stall/jitter/drift가 0이라는 뜻은 아니며 이를 근거로 오디오 품질 백로그를 완료 처리하지 않습니다.
- 600초 render soak: 통과. 30초 간격 기록은 [soak.json](validation/2026-09-27/soak.json)에 있습니다. 장기 테스트는 이 작업 중간 빌드에서 수행했으며 이후 변경된 표지 cache/메뉴/편집 화면까지의 최종 실플레이 soak 증거는 아닙니다.
- 메인/에디터 해상도 캡처와 튜토리얼 초기 화면을 육안 확인했습니다. live 키보드 입력과 스크린리더 평가를 대신하지 않습니다.
- 기준 콘텐츠 export: 세 트랙과 차트/sidecar/manifest 생성 성공.

미검증: 비-WAV 장치 비교, 사람이 듣는 타격감, 장치 분리/복구, 실제 100곡 파일 라이브러리의 초기 분석 성능, 사용자 곡 전체 UI 리플레이, 전 화면 접근성/DPI, 대체 오디오 엔진 비교. 해당 항목은 addMe.md에 유지합니다.

## 데이터 호환성과 롤백

DB 마이그레이션이나 새 의존성은 없습니다. 설정에 tutorial 필드 두 개를 추가하며 누락된 구형 설정도 읽습니다. 차트는 저장할 때만 exact extension과 `.bak`를 만듭니다. 이전 앱으로 되돌릴 때 정확한 재현이 필요하면 `.bak`도 복구합니다. 구형 리플레이는 삭제하지 않고 명시적 이유로 재생을 차단하는 정책을 유지합니다.

## 인터페이스 개편 검증 (2026-09-27)

- 별도 artifacts 경로에서 Debug/Release 컴파일 및 self-test를 실행한다.
- self-test에 화면 영역/클릭 일치·연속 곡 선택, 설정 초안 취소·저장, 노트 속성 검증·Undo를 추가했다.
- 라이브러리 4개 해상도와 글자 100/125/140%를 캡처하고, 메인·설정·결과·에디터 확대 화면도 확인한다.
- 속도 조작부를 가로 배치로 바꾸어 기존의 '값이 버튼보다 위'라는 검사 대신 실제 사각형 비중첩을 검사한다.
- 추가된 EDIT VALUES 버튼과 스냅 단위를 반영해 접근성 이름 기대값을 갱신했다.
- 실제 OS DPI와 스크린리더, 장시간 사람 입력은 이 렌더링 검증에 포함하지 않는다.
- 세부 구조와 복구 방법: [interface-design.md](interface-design.md).

검증 결과:
- Debug / Release build -warnaserror: 각각 경고 0, 오류 0.
- Debug / Release self-test: 각각 39 passed, 0 failed.
- dotnet format style --verify-no-changes --no-restore --severity warn (변경한 앱 C# 파일): 성공.
- git diff --check: 성공.
- 140% 글자 확대에서 라이브러리 행의 수직 잘림, 결과 수치의 이웃 패널 침범을 캡처로 발견하고 수정했다.
- 최종 캡처에서 최대 점수 1,000,000과 큰 콤보 숫자를 포함해 표시를 확인했다.
