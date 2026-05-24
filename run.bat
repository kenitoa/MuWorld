@echo off
chcp 65001 > nul
setlocal

set "ROOT_DIR=%~dp0"
set "APP_ROOT=%ROOT_DIR%front interface"
set "DOTNET_ROOT=%APP_ROOT%\dotnet"
set "DOTNET_EXE=%DOTNET_ROOT%\dotnet.exe"
set "PROJECT_FILE=%APP_ROOT%\RhythmGame.csproj"
set "APP_EXE=%APP_ROOT%\bin\Release\net9.0-windows\game start.exe"

if not exist "%APP_ROOT%" (
    echo front interface folder was not found.
    pause
    exit /b 1
)

if not exist "%DOTNET_EXE%" (
    echo ============================================
    echo .NET 9 SDK was not found in front interface\dotnet.
    echo Installing local .NET 9 SDK. This can take a few minutes.
    echo ============================================
    if not exist "%DOTNET_ROOT%" mkdir "%DOTNET_ROOT%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ProgressPreference='SilentlyContinue'; " ^
        "$dnDir = '%DOTNET_ROOT%'; " ^
        "$scriptPath = Join-Path $dnDir 'dotnet-install.ps1'; " ^
        "Write-Host 'Downloading dotnet-install script...'; " ^
        "Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $scriptPath -UseBasicParsing; " ^
        "Write-Host 'Installing .NET 9 SDK...'; " ^
        "& $scriptPath -Channel 9.0 -Quality ga -InstallDir $dnDir; " ^
        "Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue; " ^
        "Write-Host 'Install complete.'"
    if not exist "%DOTNET_EXE%" (
        echo .NET 9 SDK installation failed. Check the internet connection and try again.
        pause
        exit /b 1
    )
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$app = [System.IO.Path]::GetFullPath($env:APP_EXE); " ^
    "Get-Process -Name 'game start' -ErrorAction SilentlyContinue | " ^
    "Where-Object { $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $app) } | " ^
    "Stop-Process -Force"

pushd "%APP_ROOT%"
echo Building...
"%DOTNET_EXE%" build "%PROJECT_FILE%" -c Release
if %errorlevel% neq 0 (
    popd
    echo Build failed. Please check the project.
    pause
    exit /b 1
)
popd

start "" "%APP_EXE%"
endlocal
