@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem MouseSpeed/Threshold = 0 -> 1:1 raw curve (enhance pointer precision off)
reg.exe add "HKCU\Control Panel\Mouse" /v MouseSpeed /t REG_SZ /d "0" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Mouse" /v MouseThreshold1 /t REG_SZ /d "0" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Mouse" /v MouseThreshold2 /t REG_SZ /d "0" /f >nul 2>&1
exit /b 0

:restore
reg.exe add "HKCU\Control Panel\Mouse" /v MouseSpeed /t REG_SZ /d "1" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Mouse" /v MouseThreshold1 /t REG_SZ /d "6" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Mouse" /v MouseThreshold2 /t REG_SZ /d "10" /f >nul 2>&1
exit /b 0
