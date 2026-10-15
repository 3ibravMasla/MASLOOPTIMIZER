@echo off
setlocal
set "PWR=HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem HiberbootEnabled = 0 -> real kernel reload on shutdown (no inherited driver/DPC bugs)
reg.exe add "%PWR%" /v HiberbootEnabled /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%PWR%" /v HiberbootEnabled /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
