@echo off
REM Register ASCOM.OnStepX.Telescope InprocServer via regasm (requires admin).
REM Uses /codebase so the DLL can live outside the GAC.
REM Runs BOTH 64-bit and 32-bit regasm so x64 (NINA, SGP) and x86 (PHD2, CdC)
REM clients can both activate the CLSID. Without the 32-bit pass, x86 clients
REM fail with "Could not establish instance of OnStepX Telescope Driver".
set DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.Telescope.dll
if not exist "%DLL%" (
  echo Cannot find %DLL%. Build the Release solution first.
  exit /b 1
)

set REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe

if exist "%REGASM64%" (
  echo Registering 64-bit view...
  "%REGASM64%" /codebase "%DLL%" || exit /b 1
)
if exist "%REGASM32%" (
  echo Registering 32-bit view (Wow6432Node)...
  "%REGASM32%" /codebase "%DLL%" || exit /b 1
)
