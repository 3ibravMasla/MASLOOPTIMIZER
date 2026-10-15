@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Unhide the "Processor performance boost mode" setting
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\be337238-0d82-4146-a960-4f3749d470c7" /v Attributes /t REG_DWORD /d 2 /f >nul 2>&1
rem PERFBOOSTMODE = 2 -> Aggressive
powercfg.exe -setacvalueindex scheme_current sub_processor PERFBOOSTMODE 2 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v CpuBoostAggressive /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
rem PERFBOOSTMODE = 1 -> Enabled (Windows default)
powercfg.exe -setacvalueindex scheme_current sub_processor PERFBOOSTMODE 1 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v CpuBoostAggressive /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
