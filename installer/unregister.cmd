@echo off
set DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.Telescope.dll
if not exist "%DLL%" (
  echo Cannot find %DLL%.
  exit /b 1
)
set REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
if not exist "%REGASM%" set REGASM=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe
"%REGASM%" /unregister "%DLL%"
