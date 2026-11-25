@echo off
rem Builds WinUltra.exe: one file with the icon inside. It runs from wherever it is kept and shows a
rem setup window on first run, where each part (start-up, shortcut, clipboard history, notification
rem pop-ups, frosted glass, Caps Lock pop-up) is switched on or off by the person using it.
rem Needs only what ships with Windows 11 plus the Windows SDK metadata for the WinRT APIs.
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF

rem Find Windows.winmd. Set WINMD yourself to override; otherwise use the newest SDK that has it.
if not defined WINMD set "KITS=%ProgramFiles(x86)%\Windows Kits\10\UnionMetadata"
if not defined WINMD for /d %%d in ("%KITS%\10.*") do if exist "%%d\Windows.winmd" set "WINMD=%%d\Windows.winmd"
if not defined WINMD set "WINMD=%KITS%\10.0.26100.0\Windows.winmd"
if not exist "%WINMD%" (
    echo Could not find Windows.winmd. Install the Windows SDK, or set WINMD to its path.
    exit /b 1
)

if not exist WinUltra.ico powershell -NoProfile -File MakeIcon.ps1

rem Every .cs file under src is part of the app; new files are picked up automatically.
rem A response file is used so paths containing "!" or other cmd metacharacters survive.
setlocal
set "RSP=%TEMP%\winultra-sources-%RANDOM%.rsp"
type nul > "%RSP%"
for /r "src" %%f in (*.cs) do echo "%%f">>"%RSP%"

rem Three builds. They are the same app and differ only in how smooth the animations are:
rem   WinUltra.exe          120 frames per second, the standard build
rem   WinUltra-HighFPS.exe   144 frames per second, for 144 Hz and faster screens
rem   WinUltra-LowFPS.exe     60 frames per second, for slower or older PCs
rem The symbols are in quotes because cmd treats a bare semicolon as an argument separator.
call :build WinUltra.exe "FRIENDS"
call :build WinUltra-HighFPS.exe "FRIENDS;HIGHFPS"
call :build WinUltra-LowFPS.exe "FRIENDS;LOWFPS"
del "%RSP%" >nul 2>nul
endlocal
exit /b

:build
rem %1 = output file, %2 = compile symbols
"%FW%\csc.exe" /nologo /optimize+ /target:winexe /platform:x64 /define:%~2 /out:%~1 /win32icon:WinUltra.ico /win32manifest:WinUltra.manifest ^
 /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" ^
 /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Drawing.dll" ^
 /r:"%FW%\System.Management.dll" /r:"%FW%\System.Security.dll" /r:"%FW%\System.Web.Extensions.dll" /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll" ^
 /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
 /r:"%WINMD%" ^
 /resource:WinUltra.ico,WinUltra.ico ^
 @"%RSP%"
