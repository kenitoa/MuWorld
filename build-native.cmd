@echo off
setlocal
set "DOTNET=%~dp0.tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
"%DOTNET%" publish "%~dp0native\MuWorld.Native\MuWorld.Native.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "%~dp0release\MuWorld-Native-win-x64" --nologo
exit /b %errorlevel%
