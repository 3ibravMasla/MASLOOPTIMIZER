@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Disk idle timeout = 0 (Never) -> no SSD/NVMe wake-up micro-freezes
powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT 0012ee47-9041-4b5d-9b77-535fba8b1442 6738e2c4-e8a5-4a42-b16a-e040e769756e 0 >nul 2>&1
powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT 0012ee47-9041-4b5d-9b77-535fba8b1442 6738e2c4-e8a5-4a42-b16a-e040e769756e 0 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v DiskIdleOff /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT 0012ee47-9041-4b5d-9b77-535fba8b1442 6738e2c4-e8a5-4a42-b16a-e040e769756e 1200 >nul 2>&1
powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT 0012ee47-9041-4b5d-9b77-535fba8b1442 6738e2c4-e8a5-4a42-b16a-e040e769756e 1200 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v DiskIdleOff /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
