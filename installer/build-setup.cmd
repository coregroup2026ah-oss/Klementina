@echo off
rem Builds the app, then the installer (dist\Klementina-Setup-*.exe). Needs Inno Setup 6.
setlocal
cd /d "%~dp0"
call ..\build.cmd || exit /b 1
..\bin\Klementina.exe --installer-art art || exit /b 1
set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
"%ISCC%" /Q Klementina.iss || exit /b 1
echo Setup: %~dp0..\dist
