@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem NoDriveTypeAutoRun = 255 -> autorun/autoplay blocked on ALL drive types (USB malware protection)
reg.exe add "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoDriveTypeAutoRun /t REG_DWORD /d 255 /f >nul 2>&1
reg.exe add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoDriveTypeAutoRun /t REG_DWORD /d 255 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoDriveTypeAutoRun /f >nul 2>&1
reg.exe delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoDriveTypeAutoRun /f >nul 2>&1
exit /b 0
