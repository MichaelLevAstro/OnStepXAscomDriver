@echo off
REM Register ASCOM.OnStepX.Telescope InprocServer via regasm (requires admin).
REM Uses /codebase so the DLL can live outside the GAC.
set DLL=%~dp0..\src\OnStepX.Driver\bin\Release\ASCOM.OnStepX.Telescope.dll
if not exist "%DLL%" (
  echo Cannot find %DLL%. Build the Release solution first.
  exit /b 1
)
set REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
if not exist "%REGASM%" set REGASM=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe
"%REGASM%" /codebase "%DLL%"
