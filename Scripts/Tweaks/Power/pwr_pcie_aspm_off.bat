@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem PCIe Link State Power Management = 0 (Off) -> max GPU/NVMe PCIe bandwidth
powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0 >nul 2>&1
powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0 >nul 2>&1
powercfg.exe /setactive SCHEME_CURRENT >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v PcieAspmOff /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 2 >nul 2>&1
powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 2 >nul 2>&1
powercfg.exe /setactive SCHEME_CURRENT >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v PcieAspmOff /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
