@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem No Wi-Fi Sense hotspot sharing and no background location tracking
reg.exe add "HKLM\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config" /v AllowAutoConnectToWiFiSenseHotspots /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors" /v DisableLocation /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "HKLM\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config" /v AllowAutoConnectToWiFiSenseHotspots /f >nul 2>&1
reg.exe delete "HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors" /v DisableLocation /f >nul 2>&1
exit /b 0
