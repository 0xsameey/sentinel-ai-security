@echo off
setlocal
set "SENTINEL_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%SENTINEL_VSWHERE%" exit /b 2
for /f "usebackq tokens=*" %%i in (`"%SENTINEL_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "SENTINEL_VS=%%i"
if not defined SENTINEL_VS exit /b 2
call "%SENTINEL_VS%\Common7\Tools\VsDevCmd.bat" -arch=amd64 -host_arch=amd64 >nul
if errorlevel 1 exit /b 2
set "CARGO_PROFILE_DEV_DEBUG=0"
set "CARGO_BUILD_JOBS=4"
"%USERPROFILE%\.cargo\bin\cargo.exe" build --offline --locked --manifest-path "%~dp0..\tools\wasm-artifact-opt\Cargo.toml"
exit /b %errorlevel%
