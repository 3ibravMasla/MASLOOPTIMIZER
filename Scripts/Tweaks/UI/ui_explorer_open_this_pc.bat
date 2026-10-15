@echo off
setlocal
set "ADV=HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem LaunchTo = 1 -> File Explorer opens "This PC" instead of Home/Quick access
reg.exe add "%ADV%" /v LaunchTo /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%ADV%" /v ShowRecent /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%ADV%" /v ShowFrequent /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%ADV%" /v LaunchTo /t REG_DWORD /d 2 /f >nul 2>&1
reg.exe add "%ADV%" /v ShowRecent /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "%ADV%" /v ShowFrequent /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
