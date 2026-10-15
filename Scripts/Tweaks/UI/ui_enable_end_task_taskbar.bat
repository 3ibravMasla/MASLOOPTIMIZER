@echo off
setlocal
set "DEV=HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Windows 11 developer "End Task" entry in the taskbar right-click menu
reg.exe add "%DEV%" /v TaskbarEndTask /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%DEV%" /v TaskbarEndTask /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
