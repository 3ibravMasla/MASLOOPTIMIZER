@echo off
setlocal
set "ADV=HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
set "POL=HKCU\Software\Policies\Microsoft\Windows\Explorer"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Remove Widgets (weather/news), Task View and Chat icons from the taskbar
reg.exe add "%ADV%" /v TaskbarDa /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%ADV%" /v ShowTaskViewButton /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%ADV%" /v TaskbarMn /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%POL%" /v AllowNewsAndInterests /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%ADV%" /v TaskbarDa /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%ADV%" /v ShowTaskViewButton /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%ADV%" /v TaskbarMn /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe delete "%POL%" /v AllowNewsAndInterests /f >nul 2>&1
exit /b 0
