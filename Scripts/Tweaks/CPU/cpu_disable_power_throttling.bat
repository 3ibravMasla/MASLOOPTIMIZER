@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power" /v PowerThrottlingOff /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power" /v PowerThrottlingOff /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
