@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Fixed platform timer tick + no synthetic timers -> stable 1% Low frametimes
bcdedit.exe /set useplatformtick yes >nul 2>&1
bcdedit.exe /set disabledynamictick yes >nul 2>&1
bcdedit.exe /deletevalue useplatformclock >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v KernelTimerTuned /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
bcdedit.exe /deletevalue useplatformtick >nul 2>&1
bcdedit.exe /deletevalue disabledynamictick >nul 2>&1
bcdedit.exe /deletevalue useplatformclock >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v KernelTimerTuned /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
