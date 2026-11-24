@echo off
rem Builds Klementina.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No downloads needed.
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set RA=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8
set WM=%WINDIR%\System32\WinMetadata
set RUNTIME=%RA%\Facades\System.Runtime.dll
if not exist "%RUNTIME%" set RUNTIME=%FW%\System.Runtime.dll
set WRTINTEROP=%RA%\Facades\System.Runtime.InteropServices.WindowsRuntime.dll
if not exist "%WRTINTEROP%" set WRTINTEROP=%FW%\System.Runtime.InteropServices.WindowsRuntime.dll
if not exist bin mkdir bin

rem bundled third-party pieces next to the exe
copy /y vendor\zxing\zxing.dll bin\ >nul
if not exist bin\qpdf mkdir bin\qpdf
xcopy /y /d /q vendor\qpdf\* bin\qpdf\ >nul
if not exist bin\ffmpeg mkdir bin\ffmpeg
xcopy /y /d /q vendor\ffmpeg\* bin\ffmpeg\ >nul

set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll /r:Microsoft.VisualBasic.dll ^
 /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
 /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\WindowsFormsIntegration.dll" /r:System.Xaml.dll /r:System.Web.Extensions.dll ^
 /r:"%RUNTIME%" /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%WRTINTEROP%" ^
 /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Data.winmd" /r:"%WM%\Windows.Storage.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Graphics.winmd" /r:"%WM%\Windows.Globalization.winmd" /r:"%WM%\Windows.ApplicationModel.winmd" ^
 /r:vendor\zxing\zxing.dll
rem interface languages: every lang\*.txt is embedded as resource "lang.<file>"
set RES=
for %%f in (lang\*.txt) do call set RES=%%RES%% /resource:"%%f",lang.%%~nxf
set OPTS=/nologo /target:winexe /platform:anycpu /optimize+ /nowarn:1701,1702 /utf8output

rem pass 1: build, then let the app draw its own icon
"%FW%\csc.exe" %OPTS% %REFS% %RES% /out:bin\Klementina.exe src\*.cs || goto :fail
bin\Klementina.exe --write-icon bin\Klementina.ico || goto :fail
rem pass 2: rebuild with the icon embedded
"%FW%\csc.exe" %OPTS% %REFS% %RES% /win32icon:bin\Klementina.ico /out:bin\Klementina.exe src\*.cs || goto :fail
echo.
echo Built: %~dp0bin\Klementina.exe
exit /b 0
:fail
echo Build failed.
exit /b 1
