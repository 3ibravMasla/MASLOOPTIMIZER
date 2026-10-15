@echo off
setlocal
set "CIC=HKLM\SYSTEM\CurrentControlSet\Control\CI\Config"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem VulnerableDriverBlocklistEnable = 0 -> tuning drivers (Afterburner, Ryzen Master, FanControl) load again
reg.exe add "%CIC%" /v VulnerableDriverBlocklistEnable /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%CIC%" /v VulnerableDriverBlocklistEnable /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
