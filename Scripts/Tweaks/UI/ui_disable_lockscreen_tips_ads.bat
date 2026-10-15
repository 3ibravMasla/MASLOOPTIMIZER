@echo off
setlocal
set "CDM=HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem No lock screen "fun facts", tips and suggested-app ads
reg.exe add "%CDM%" /v RotatingLockScreenOverlayEnabled /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%CDM%" /v SubscribedContent-338387Enabled /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%CDM%" /v SubscribedContent-338389Enabled /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%CDM%" /v SoftLandingEnabled /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%CDM%" /v RotatingLockScreenOverlayEnabled /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%CDM%" /v SubscribedContent-338387Enabled /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%CDM%" /v SubscribedContent-338389Enabled /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%CDM%" /v SoftLandingEnabled /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
