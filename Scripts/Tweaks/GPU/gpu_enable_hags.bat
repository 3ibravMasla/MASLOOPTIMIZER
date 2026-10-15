@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem HwSchMode = 2 -> Hardware-Accelerated GPU Scheduling enabled (Win10 2004+)
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" /v HwSchMode /t REG_DWORD /d 2 /f >nul 2>&1
exit /b 0

:restore
rem 1 = disabled (Windows default)
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" /v HwSchMode /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
