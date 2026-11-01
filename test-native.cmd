@echo off
setlocal
set "DOTNET=%~dp0.tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
"%DOTNET%" run --project "%~dp0native\MuWorld.Native.Tests\MuWorld.Native.Tests.csproj" --configuration Release
exit /b %errorlevel%
