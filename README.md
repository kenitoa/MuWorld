# MuWorld · Native Rhythm Studio

Windows 네이티브 오프라인 리듬게임입니다. 화면은 **C# / WPF**, 오디오는 **WASAPI**, 키보드는 별도 스레드의 **Windows Raw Input**으로 처리합니다. 배포 앱에 Electron, Chromium, WebView, HTML/JavaScript 실행기는 포함하지 않습니다. 서버·계정·네트워크 연결 없이 플레이합니다.

## 실행

프로젝트의 **`MuWorld 실행.cmd`** 또는 다음 실행 파일을 더블 클릭하세요.

```text
release/MuWorld-Native-win-x64/MuWorld.exe
```

배포 폴더 전체를 함께 보관해야 합니다. `Assets/content.json`이 곡과 채보 데이터입니다. .NET 런타임은 포함되어 있어 플레이할 컴퓨터에 .NET SDK나 Node.js를 설치할 필요가 없습니다. 현재 빌드는 Windows x64용입니다.

## 플레이

| 조작 | 동작 |
|---|---|
| D · F · J · K | 왼쪽부터 네 레인 입력 |
| 긴 노트 | 시작에 맞춰 누르고 끝까지 유지 |
| ESC | 일시정지 / 재개 |
| F11 | 전체 화면 전환 |
| 음악 화면 ↑ · ↓ | 곡 선택 변경 |
| 음악 화면 Enter | 선택한 채보 시작 |
| 보정 화면 Space | 들리는 박자에 맞춰 입력 |

환경 설정에서 키를 바꿀 수 있습니다. 창이 비활성화되면 일시정지합니다. 일시정지한 플레이는 기록에 표시되며 일반 최고 기록과 동일 조건 성장 비교에서 제외합니다.

## 구현된 기능

- 차분한 슬레이트·라임·라벤더 색상, 네이티브 벡터 앨범 아트 5종, 4레인 집중 화면.
- 오리지널 합성 음악 5곡, 난이도별 채보 15개, 패턴 연습 12개, 실습 가이드 4단계.
- 50–100% 속도 조절과 음높이 유지, 구간 연습, 1·3·5회 반복, 메트로놈, 3회 연속 95% 달성 시 속도 상승.
- 평균·중앙값·표준편차·MAD·MAE·P95, 입력 분포, 레인·패턴·구간·홀드·동시치기 분석과 추천 연습.
- 실제 입력 리플레이, 같은 조건의 성장 그래프, 실제 기록으로 달성하는 성취 배지, 판정별 CSV.
- 에코 메모리, 사일런트 스텝, 리듬 공방, 펄스 가드, 비트 루트의 다섯 가지 아케이드.
- 밝기·레인 폭·효과·접근 시간·메뉴 크기·키 배치, 입력/시각 보정, 32박 보정, 장치별 프로필.
- 로컬 JSON 저장, 원자적 파일 교체와 이전 백업, JSON 백업 내보내기·가져오기.

아케이드는 각기 다른 규칙을 사용합니다. 에코의 응답 단계에는 정답 노트를 표시하지 않고, 무음 모드에서는 실시간 판정과 점수의 박자 단서를 숨깁니다. 루트 모드는 계획한 네 방향을 기억해 5×5 보드를 이동합니다.

## 타이밍의 의미

입력 보정 **+5 ms는 측정 편차에서 정확히 5,000 µs를 한 번 빼는 설정**입니다. 입력값과 채보는 정수 마이크로초로 저장하고 배속·판정 경계는 decimal 연산으로 비교합니다. 기본 PERFECT는 ±20 ms, 연습의 정밀 판정은 ±5 ms입니다. 시각 보정은 판정에 관여하지 않습니다.

이 수치가 키보드 접점에서 실제 소리가 귀에 도달하기까지의 **물리적 오차 0 ms를 보장하지는 않습니다**. 앱은 Raw Input을 받는 순간의 QPC 시각과 실제 WASAPI 장치 시계를 연결합니다. 키보드 스캔, 드라이버, 디스플레이, 출력 장치의 지연과 변동은 별도 측정 대상입니다. 32박 보정에는 사람의 반응 습관도 포함되며, 결과는 직접 적용해야 저장됩니다.

## 데이터

```text
%LOCALAPPDATA%/MuWorldNative/library.json
%LOCALAPPDATA%/MuWorldNative/library.backup.json
```

백업 가져오기는 검증 후 현재 데이터를 교체합니다. 직전 데이터는 `library.backup.json`에 보존됩니다. 외부 서버로 데이터를 보내지 않습니다. 이전 Electron 시제품의 소스는 `legacy/electron`에 보관하며 현재 앱에서 사용하지 않습니다. 이전 시제품의 사용자 저장소를 변경하지 않습니다.

## 개발과 검증

Windows용 .NET 10 SDK가 필요합니다. 현재 작업 폴더에는 공식 배포본의 SHA-512를 확인한 SDK를 `.tools/dotnet`에 준비했습니다. `build-native.cmd`와 `test-native.cmd`는 이 SDK를 우선 사용하며, 없으면 PATH의 `dotnet`을 사용합니다.

```powershell
.\test-native.cmd
.\build-native.cmd
```

첫 빌드의 NuGet 패키지 복원에는 인터넷이 필요할 수 있습니다. 배포 앱의 실행에는 필요하지 않습니다. 테스트는 별도 디렉터리를 사용합니다.

```powershell
# 실행 파일 통합 점검 및 WPF 화면 캡처 (자동 종료)
.\release\MuWorld-Native-win-x64\MuWorld.exe --smoke
```

결과는 `artifacts/native/smoke-result.txt`, 화면은 같은 폴더의 PNG에 저장합니다. 이 모드는 `%TEMP%/MuWorldNative-QA`만 사용하므로 실제 프로필과 분리됩니다.

코어 테스트 **36개 통과**: 판정 경계, ±5 ms, decimal 배속, 홀드, 늦게 도착한 입력, FPS 독립성, 분석, 백업, QPC/WASAPI 변환, 실제 합성 음높이 보존을 확인했습니다. 실제 WASAPI 출력·Raw Input 키 입력·일시정지·재개·완주·리플레이·아케이드 시작도 Windows에서 확인했습니다. 상세 구현과 한계는 [네이티브 아키텍처 문서](native/docs/ARCHITECTURE.md)를 참고하세요.

`Assets/content.json`은 소스에 포함되어 있어 일반 빌드에 Node.js가 필요하지 않습니다. 원래 작곡/채보 생성 데이터를 수정해 다시 내보낼 때만 선택적으로 `node scripts/export-native-content.mjs`를 사용합니다.

서드파티 고지는 `native/MuWorld.Native/Assets/Licenses`, Pretendard 글꼴 라이선스는 `Assets/Fonts/OFL.txt`에 포함합니다.
