@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
sc.exe stop DiagTrack >nul 2>&1
sc.exe config DiagTrack start= disabled >nul 2>&1
sc.exe stop dmwappushservice >nul 2>&1
sc.exe config dmwappushservice start= disabled >nul 2>&1
reg.exe add "HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" /v AllowTelemetry /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
sc.exe config DiagTrack start= auto >nul 2>&1
sc.exe start DiagTrack >nul 2>&1
sc.exe config dmwappushservice start= demand >nul 2>&1
reg.exe add "HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" /v AllowTelemetry /t REG_DWORD /d 3 /f >nul 2>&1
reg.exe delete "HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" /v AllowTelemetry /f >nul 2>&1
exit /b 0
