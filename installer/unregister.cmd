@echo off
REM Unregister both the new combined DLL (ASCOM.OnStepX.dll) and any
REM legacy 0.4.x Telescope-only DLL (ASCOM.OnStepX.Telescope.dll). Failures
REM on a missing DLL are silenced so the script is idempotent.
set NEW_DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.dll
set OLD_DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.Telescope.dll

set REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe

if exist "%NEW_DLL%" (
  if exist "%REGASM64%" "%REGASM64%" /unregister "%NEW_DLL%"
  if exist "%REGASM32%" "%REGASM32%" /unregister "%NEW_DLL%"
)

if exist "%OLD_DLL%" (
  if exist "%REGASM64%" "%REGASM64%" /unregister "%OLD_DLL%"
  if exist "%REGASM32%" "%REGASM32%" /unregister "%OLD_DLL%"
)
