@echo off
setlocal EnableDelayedExpansion

rem OnStepX ASCOM Driver (WPF Hub) - build Release + compile Inno Setup installer.
rem
rem Mirrors build-installer.cmd but targets the new OnStepX.Hub.Wpf project and
rem the OnStepX.AscomDriver.Wpf.iss script. Output: installer\OnStepX-Setup-Wpf-*.exe
rem
rem Usage:
rem   build-installer-wpf.cmd                       -> Release, version from .iss
rem   build-installer-wpf.cmd 0.4.0                 -> Release, version 0.4.0
rem   build-installer-wpf.cmd Debug                 -> Debug build (skips installer)
rem   build-installer-wpf.cmd Release 0.4.0         -> explicit config + version
rem   build-installer-wpf.cmd 0.4.0 Release         -> order-independent

set CONFIG=Release
set VERSION=

call :classify "%~1"
call :classify "%~2"

set ROOT=%~dp0
set SOLUTION=%ROOT%OnStepX.sln
set ISS=%ROOT%installer\OnStepX.AscomDriver.Wpf.iss

rem Locate ISCC (Inno Setup 6, 32-bit or 64-bit install).
set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    echo [ERROR] Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php
    exit /b 2
)

echo === [1/2] dotnet build (%CONFIG%) — solution build, WPF hub + driver + shared ===
if defined VERSION (
    dotnet build "%SOLUTION%" -c %CONFIG% -nologo -v minimal -p:Version=%VERSION% -p:AssemblyVersion=%VERSION%.0 -p:FileVersion=%VERSION%.0
) else (
    dotnet build "%SOLUTION%" -c %CONFIG% -nologo -v minimal
)
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

if /I not "%CONFIG%"=="Release" (
    echo [WARN] Installer script expects Release artifacts. Skipping ISCC.
    exit /b 0
)

echo.
echo === [2/2] Inno Setup compile (WPF) ===
rem Sweep prior WPF installer outputs so installer\ never accumulates stale
rem siblings when the version number changes between builds.
del /q "%ROOT%installer\OnStepX-Setup-Wpf-*.exe" >nul 2>&1

rem Retry loop. EndUpdateResource(110) fires when Windows Defender scans the
rem freshly-written setup exe just as ISCC tries to close its resource handle.
rem Three attempts with a short delay between is enough in practice.
set ATTEMPT=0
:isccretry
set /a ATTEMPT+=1
if defined VERSION (
    "%ISCC%" /DMyAppVersion=%VERSION% /DDriverVersion=%VERSION%.0 "%ISS%"
) else (
    "%ISCC%" "%ISS%"
)
if not errorlevel 1 goto isccdone
if %ATTEMPT% GEQ 3 (
    echo [ERROR] Installer compile failed after %ATTEMPT% attempts.
    exit /b 1
)
echo [WARN] ISCC failed (attempt %ATTEMPT%), retrying in 2s...
del /q "%ROOT%installer\OnStepX-Setup-Wpf-*.exe" >nul 2>&1
timeout /t 2 /nobreak >nul
goto isccretry
:isccdone

echo.
echo === Done ===
for %%F in ("%ROOT%installer\OnStepX-Setup-Wpf-*.exe") do echo Output: %%~fF
endlocal
exit /b 0

rem ----- subroutines -----

:classify
set _arg=%~1
if "%_arg%"=="" exit /b 0
if /I "%_arg%"=="Debug"   ( set CONFIG=Debug   & exit /b 0 )
if /I "%_arg%"=="Release" ( set CONFIG=Release & exit /b 0 )
set VERSION=%_arg%
exit /b 0
