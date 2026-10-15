@echo off
setlocal
set "MM=HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Keep kernel and drivers in physical RAM instead of paging them to disk
reg.exe add "%MM%" /v LargeSystemCache /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%MM%" /v DisablePagingExecutive /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%MM%" /v LargeSystemCache /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%MM%" /v DisablePagingExecutive /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
