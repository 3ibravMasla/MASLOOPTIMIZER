@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem SMB1 = 0 -> vulnerable legacy SMBv1 server protocol disabled
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters" /v SMB1 /t REG_DWORD /d 0 /f >nul 2>&1
sc.exe config lanmanworkstation depend= bowser/mrxsmb20/nsi >nul 2>&1
sc.exe config mrxsmb10 start= disabled >nul 2>&1
exit /b 0

:restore
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters" /v SMB1 /t REG_DWORD /d 1 /f >nul 2>&1
sc.exe config mrxsmb10 start= demand >nul 2>&1
sc.exe config lanmanworkstation depend= bowser/mrxsmb10/mrxsmb20/nsi >nul 2>&1
exit /b 0
