@echo off
setlocal
set "DNS=HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem EnableMulticast = 0 -> LLMNR disabled, killing LLMNR/NBT-NS/WPAD credential relay vectors
reg.exe add "%DNS%" /v EnableMulticast /t REG_DWORD /d 0 /f >nul 2>&1
rem WPAD: no automatic proxy discovery over the network
reg.exe add "%DNS%" /v DisableSmartNameResolution /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters" /v EnableMulticast /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "%DNS%" /v EnableMulticast /f >nul 2>&1
reg.exe delete "%DNS%" /v DisableSmartNameResolution /f >nul 2>&1
reg.exe delete "HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters" /v EnableMulticast /f >nul 2>&1
exit /b 0
