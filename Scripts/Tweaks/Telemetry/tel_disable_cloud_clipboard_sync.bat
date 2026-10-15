@echo off
setlocal
set "SYS=HKLM\SOFTWARE\Policies\Microsoft\Windows\System"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem No clipboard/activity history sync to Microsoft cloud
reg.exe add "%SYS%" /v AllowCrossDeviceClipboard /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%SYS%" /v EnableActivityFeed /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%SYS%" /v PublishUserActivities /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%SYS%" /v UploadUserActivities /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "%SYS%" /v AllowCrossDeviceClipboard /f >nul 2>&1
reg.exe delete "%SYS%" /v EnableActivityFeed /f >nul 2>&1
reg.exe delete "%SYS%" /v PublishUserActivities /f >nul 2>&1
reg.exe delete "%SYS%" /v UploadUserActivities /f >nul 2>&1
exit /b 0
