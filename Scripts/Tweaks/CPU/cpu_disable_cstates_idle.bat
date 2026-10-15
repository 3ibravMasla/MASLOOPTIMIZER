@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem IDLEDISABLE = 1 -> no deep C-state transitions, cores never go to sleep
powercfg.exe -setacvalueindex scheme_current sub_processor IDLEDISABLE 1 >nul 2>&1
powercfg.exe -setdcvalueindex scheme_current sub_processor IDLEDISABLE 1 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v CpuIdleDisable /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
powercfg.exe -setacvalueindex scheme_current sub_processor IDLEDISABLE 0 >nul 2>&1
powercfg.exe -setdcvalueindex scheme_current sub_processor IDLEDISABLE 0 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v CpuIdleDisable /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
