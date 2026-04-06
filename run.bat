@echo off
chcp 65001 > nul
cd /d "%~dp0"

:: ── .NET 9 SDK 확인 (MuWorld 폴더 내) ──
set "DOTNET_ROOT=%~dp0dotnet"
set "DOTNET_EXE=%DOTNET_ROOT%\dotnet.exe"

if exist "%DOTNET_EXE%" (
    echo .NET 9 SDK 감지됨. 설치 과정을 건너뜁니다.
) else (
    echo ============================================
    echo  .NET 9 SDK가 설치되어 있지 않습니다.
    echo  MuWorld 폴더에 설치합니다... (2~3분 소요)
    echo ============================================
    if not exist "%DOTNET_ROOT%" mkdir "%DOTNET_ROOT%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ProgressPreference='SilentlyContinue'; " ^
        "$dnDir = '%DOTNET_ROOT%'; " ^
        "$scriptPath = Join-Path $dnDir 'dotnet-install.ps1'; " ^
        "Write-Host 'dotnet-install 스크립트 다운로드 중...'; " ^
        "Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $scriptPath -UseBasicParsing; " ^
        "Write-Host '.NET 9 SDK 설치 중...'; " ^
        "& $scriptPath -Channel 9.0 -Quality ga -InstallDir $dnDir; " ^
        "Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue; " ^
        "Write-Host '설치 완료.'"
    if not exist "%DOTNET_EXE%" (
        echo .NET 9 SDK 설치에 실패했습니다. 인터넷 연결을 확인해주세요.
        pause
        exit /b 1
    )
)

:: ── Build & Run ──
echo Building...
"%DOTNET_EXE%" build RhythmGame.csproj -c Release
if %errorlevel% neq 0 (
    echo Build failed. Please check the project.
    pause
    exit /b 1
)
start "" "bin\Release\net9.0-windows\game start.exe"
