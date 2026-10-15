@echo off
setlocal
set "FTH=HKLM\SOFTWARE\Microsoft\FTH"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Disable Fault Tolerant Heap (FTH):
rem after a single crash Windows attaches a slow diagnostic shim to the crashed .exe,
rem permanently degrading game performance until FTH state is cleared. Enabled = 0 keeps FTH off.
reg.exe add "%FTH%" /v Enabled /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%FTH%" /v Enabled /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
