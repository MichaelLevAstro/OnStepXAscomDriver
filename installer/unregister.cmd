@echo off
set DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.Telescope.dll
if not exist "%DLL%" (
  echo Cannot find %DLL%.
  exit /b 1
)

set REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe

if exist "%REGASM64%" "%REGASM64%" /unregister "%DLL%"
if exist "%REGASM32%" "%REGASM32%" /unregister "%DLL%"
