@echo off
setlocal
set "IFEO=HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\csrss.exe"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem csrss.exe owns raw mouse/keyboard input: High CPU priority (4) + High I/O priority (3)
reg.exe add "%IFEO%\PerfOptions" /v CpuPriorityClass /t REG_DWORD /d 4 /f >nul 2>&1
reg.exe add "%IFEO%\PerfOptions" /v IoPriority /t REG_DWORD /d 3 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "%IFEO%" /f >nul 2>&1
exit /b 0
