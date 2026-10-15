@echo off
setlocal
set "SP=HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Storage Sense master switch off -> no background disk sweeps while gaming
reg.exe add "%SP%" /v 01 /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%SP%" /v 01 /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
