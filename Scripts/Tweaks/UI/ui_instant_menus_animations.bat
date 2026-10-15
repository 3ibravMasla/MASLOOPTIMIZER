@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem MenuShowDelay = 0 -> menus open instantly
reg.exe add "HKCU\Control Panel\Desktop" /v MenuShowDelay /t REG_SZ /d "0" /f >nul 2>&1
reg.exe add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\TooltipAnimation" /v DefaultValue /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v TaskbarAnimations /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "HKCU\Control Panel\Desktop" /v MenuShowDelay /t REG_SZ /d "400" /f >nul 2>&1
reg.exe add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\TooltipAnimation" /v DefaultValue /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v TaskbarAnimations /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
