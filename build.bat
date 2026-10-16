@echo off
cd /d "%~dp0"
rem Builds KinetikHub.exe with the .NET Framework compiler that ships with Windows. No SDK needed.
rem "build.bat package" also makes KinetikHub.zip (the exe, README and licence) for a release.
if not exist icon.ico powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1 || exit /b 1
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:KinetikHub.new.exe /win32icon:icon.ico /resource:icon.ico,icon.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  KinetikHub.cs || exit /b 1
rem Built under a new name first, so this works while the app is running. The exe it replaces goes to ..\Old Versions
rem (when that folder is there), stamped with the date and time, to go back to if a build misbehaves.
if exist KinetikHub.exe if exist "..\Old Versions\" (
  for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd-HHmmss"') do move /y KinetikHub.exe "..\Old Versions\KinetikHub-%%t.exe" >nul
)
if exist KinetikHub.exe move /y KinetikHub.exe KinetikHub.old.exe >nul
move /y KinetikHub.new.exe KinetikHub.exe >nul || exit /b 1
if exist KinetikHub.old.exe del KinetikHub.old.exe 2>nul
echo Built KinetikHub.exe
if /i not "%~1"=="package" exit /b 0
if exist dist rmdir /s /q dist
mkdir dist\KinetikHub
copy /y KinetikHub.exe dist\KinetikHub\ >nul
copy /y README.md dist\KinetikHub\ >nul
copy /y LICENSE dist\KinetikHub\ >nul
if exist KinetikHub.zip del KinetikHub.zip
powershell -NoProfile -Command "Compress-Archive -Path dist\KinetikHub\* -DestinationPath KinetikHub.zip"
rmdir /s /q dist
echo Made KinetikHub.zip
