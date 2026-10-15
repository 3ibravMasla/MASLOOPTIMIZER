@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem HETEROPOLICY = 4 (prefer performance cores / 3D V-Cache CCD), SCHEDPOLICY = 2 (long threads)
powercfg.exe -setacvalueindex scheme_current sub_processor HETEROPOLICY 4 >nul 2>&1
powercfg.exe -setacvalueindex scheme_current sub_processor SCHEDPOLICY 2 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v HeteroScheduler /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
powercfg.exe -setacvalueindex scheme_current sub_processor HETEROPOLICY 0 >nul 2>&1
powercfg.exe -setacvalueindex scheme_current sub_processor SCHEDPOLICY 0 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v HeteroScheduler /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
