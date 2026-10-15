@echo off
setlocal
set "KRN=HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Windows 11: let 0.5 ms timer requests apply process-wide, not only to the requesting thread
reg.exe add "%KRN%" /v GlobalTimerResolutionRequests /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%KRN%" /v GlobalTimerResolutionRequests /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
