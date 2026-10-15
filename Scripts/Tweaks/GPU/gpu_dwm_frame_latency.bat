@echo off
setlocal
set "DWM=HKLM\SOFTWARE\Microsoft\Windows\Dwm"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Smaller DWM composition queue -> lower desktop composition latency
reg.exe add "%DWM%" /v MaxQueuedBuffers /t REG_DWORD /d 2 /f >nul 2>&1
reg.exe add "%DWM%" /v ForceEffectMode /t REG_DWORD /d 2 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "%DWM%" /v MaxQueuedBuffers /f >nul 2>&1
reg.exe delete "%DWM%" /v ForceEffectMode /f >nul 2>&1
exit /b 0
