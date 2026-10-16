@echo off
cd /d "%~dp0"
rem Builds KinetikHub.exe with the .NET Framework compiler that ships with Windows. No SDK needed.
rem "build.bat package" also makes KinetikHub.zip (the exe, README and licence) for a release.
if not exist icon.ico powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1 || exit /b 1
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:KinetikHub.new.exe /win32icon:icon.ico /resource:icon.ico,icon.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  KinetikHub.cs SuiteSupport.cs || exit /b 1
rem Keep previous binaries. Never delete recovery material during a build.
if not exist "..\Old Versions\" mkdir "..\Old Versions" || exit /b 1
if exist KinetikHub.exe (
  for /f %%t in ('powershell -NoProfile -Command "[Guid]::NewGuid().ToString('N')"') do move /y KinetikHub.exe "..\Old Versions\KinetikHub-%%t.exe" >nul
  if exist KinetikHub.exe exit /b 1
)
move /y KinetikHub.new.exe KinetikHub.exe >nul || exit /b 1
echo Built KinetikHub.exe
if /i not "%~1"=="package" exit /b 0
powershell -NoProfile -ExecutionPolicy Bypass -File package.ps1 -App KinetikHub
if errorlevel 1 exit /b 1
exit /b 0
