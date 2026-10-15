@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem No incoming Remote Assistance invitations and no Remote Desktop connections
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance" /v fAllowToGetHelp /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server" /v fDenyTSConnections /t REG_DWORD /d 1 /f >nul 2>&1
sc.exe config TermService start= disabled >nul 2>&1
netsh.exe advfirewall firewall set rule group="remote desktop" new enable=No >nul 2>&1
exit /b 0

:restore
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance" /v fAllowToGetHelp /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server" /v fDenyTSConnections /t REG_DWORD /d 0 /f >nul 2>&1
sc.exe config TermService start= demand >nul 2>&1
exit /b 0
