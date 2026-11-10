@echo off
setlocal
rem Process-local build settings only. No installs or machine PATH changes.
set "SENTINEL_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%SENTINEL_VSWHERE%" exit /b 2
for /f "usebackq tokens=*" %%i in (`"%SENTINEL_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "SENTINEL_VS=%%i"
if not defined SENTINEL_VS exit /b 2
call "%SENTINEL_VS%\Common7\Tools\VsDevCmd.bat" -arch=amd64 -host_arch=amd64 >nul
if errorlevel 1 exit /b 2
rem Official aws-lc-sys fallback for x64 Windows when NASM is absent.
set "AWS_LC_SYS_PREBUILT_NASM=1"
"%USERPROFILE%\.cargo\bin\cargo.exe" run --offline --manifest-path "%~dp0..\tools\sentinel-state-rehearsal\Cargo.toml" -- %*
exit /b %errorlevel%

