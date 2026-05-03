@echo off
REM Register ASCOM.OnStepX (Telescope + Focuser) InprocServer via regasm.
REM Requires admin. Uses /codebase so the DLL can live outside the GAC.
REM Runs BOTH 64-bit and 32-bit regasm so x64 (NINA, SGP) and x86 (PHD2, CdC)
REM clients can both activate the CLSIDs. Without the 32-bit pass, x86 clients
REM fail with "Could not establish instance of OnStepX Telescope/Focuser Driver".
REM
REM A single DLL now hosts both drivers (ASCOM.OnStepX.Telescope +
REM ASCOM.OnStepX.Focuser ProgIDs). One regasm call registers both.
set DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.dll
if not exist "%DLL%" (
  echo Cannot find %DLL%. Build the Release solution first.
  exit /b 1
)

REM Clean up the pre-0.5 DLL name if a prior install registered it. Harmless
REM no-op if regasm reports it isn't registered.
set OLD_DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.Telescope.dll

set REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe

if exist "%OLD_DLL%" (
  if exist "%REGASM64%" (
    echo Unregistering legacy 64-bit Telescope-only DLL...
    "%REGASM64%" /unregister "%OLD_DLL%" >nul 2>&1
  )
  if exist "%REGASM32%" (
    echo Unregistering legacy 32-bit Telescope-only DLL...
    "%REGASM32%" /unregister "%OLD_DLL%" >nul 2>&1
  )
)

if exist "%REGASM64%" (
  echo Registering 64-bit view...
  "%REGASM64%" /codebase "%DLL%" || exit /b 1
)
if exist "%REGASM32%" (
  echo Registering 32-bit view (Wow6432Node)...
  "%REGASM32%" /codebase "%DLL%" || exit /b 1
)
