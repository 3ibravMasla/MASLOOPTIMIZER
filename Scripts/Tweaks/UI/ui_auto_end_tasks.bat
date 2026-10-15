@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Close hung apps instantly on shutdown/reboot instead of waiting
reg.exe add "HKCU\Control Panel\Desktop" /v AutoEndTasks /t REG_SZ /d "1" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Desktop" /v HungAppTimeout /t REG_SZ /d "1000" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Desktop" /v WaitToKillAppTimeout /t REG_SZ /d "2000" /f >nul 2>&1
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control" /v WaitToKillServiceTimeout /t REG_SZ /d "2000" /f >nul 2>&1
exit /b 0

:restore
reg.exe add "HKCU\Control Panel\Desktop" /v AutoEndTasks /t REG_SZ /d "0" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Desktop" /v HungAppTimeout /t REG_SZ /d "5000" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Desktop" /v WaitToKillAppTimeout /t REG_SZ /d "5000" /f >nul 2>&1
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control" /v WaitToKillServiceTimeout /t REG_SZ /d "5000" /f >nul 2>&1
exit /b 0
