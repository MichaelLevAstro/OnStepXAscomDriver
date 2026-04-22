@echo off
setlocal EnableDelayedExpansion

rem OnStepX ASCOM Driver - build Release + compile Inno Setup installer.
rem
rem Usage:
rem   build-installer.cmd                       -> Release, version from .iss
rem   build-installer.cmd 0.4.0                 -> Release, version 0.4.0
rem   build-installer.cmd Debug                 -> Debug build (skips installer)
rem   build-installer.cmd Release 0.4.0         -> explicit config + version
rem   build-installer.cmd 0.4.0 Release         -> order-independent

set CONFIG=Release
set VERSION=

call :classify "%~1"
call :classify "%~2"

set ROOT=%~dp0
set SOLUTION=%ROOT%OnStepX.sln
set ISS=%ROOT%installer\OnStepX.AscomDriver.iss

rem Locate ISCC (Inno Setup 6, 32-bit or 64-bit install).
set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    echo [ERROR] Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php
    exit /b 2
)

echo === [1/2] dotnet build (%CONFIG%) — three projects via OnStepX.sln ===
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
echo === [2/2] Inno Setup compile ===
rem Sweep any prior OnStepX-Setup-*.exe so installer\ never accumulates stale
rem siblings when the version number changes between builds.
del /q "%ROOT%installer\OnStepX-Setup-*.exe" >nul 2>&1
if defined VERSION (
    echo Overriding installer version: %VERSION%  [DriverVersion=%VERSION%.0]
    "%ISCC%" /DMyAppVersion=%VERSION% /DDriverVersion=%VERSION%.0 "%ISS%"
) else (
    "%ISCC%" "%ISS%"
)
if errorlevel 1 (
    echo [ERROR] Installer compile failed.
    exit /b 1
)

echo.
echo === Done ===
for %%F in ("%ROOT%installer\OnStepX-Setup-*.exe") do echo Output: %%~fF
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
