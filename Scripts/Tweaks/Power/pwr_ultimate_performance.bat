@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Unlock + activate the hidden "Ultimate Performance" plan
powercfg.exe -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 >nul 2>&1
powercfg.exe /setactive e9a42b02-d5df-448d-aa00-03f14749eb61 >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v UltimatePower /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
rem Back to Balanced
powercfg.exe /setactive 381b4222-f694-41f0-9685-ff5bb260df2e >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v UltimatePower /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
