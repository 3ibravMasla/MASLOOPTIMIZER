@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
reg.exe add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting" /v Disabled /t REG_DWORD /d 1 /f >nul 2>&1
sc.exe stop WerSvc >nul 2>&1
sc.exe config WerSvc start= disabled >nul 2>&1
exit /b 0

:restore
reg.exe add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting" /v Disabled /t REG_DWORD /d 0 /f >nul 2>&1
sc.exe config WerSvc start= demand >nul 2>&1
sc.exe start WerSvc >nul 2>&1
exit /b 0
