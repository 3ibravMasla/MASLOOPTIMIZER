@echo off
setlocal
set "WSH=HKCU\Software\Microsoft\Windows Script Host\Settings"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Enabled = 0 -> .vbs/.js droppers can no longer execute via Windows Script Host
reg.exe add "%WSH%" /v Enabled /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "%WSH%" /v Enabled /f >nul 2>&1
exit /b 0
