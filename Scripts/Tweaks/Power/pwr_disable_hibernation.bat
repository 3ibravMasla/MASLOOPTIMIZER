@echo off
setlocal
set "PWR=HKLM\SYSTEM\CurrentControlSet\Control\Power"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Free several GB of hiberfil.sys on SSD: full hibernation off.
rem powercfg /h off sets HibernateEnabled = 0 under Control\Power (distinct from Fast Startup's HiberbootEnabled).
powercfg.exe /h off >nul 2>&1
reg.exe add "%PWR%" /v HibernateEnabled /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
powercfg.exe /h on >nul 2>&1
reg.exe add "%PWR%" /v HibernateEnabled /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
