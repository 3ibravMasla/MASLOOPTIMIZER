@echo off
setlocal
set "SYS=HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
set "GAMES=HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
reg.exe add "%SYS%" /v SystemResponsiveness /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "%SYS%" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f >nul 2>&1
reg.exe add "%GAMES%" /v "GPU Priority" /t REG_DWORD /d 8 /f >nul 2>&1
reg.exe add "%GAMES%" /v "Priority" /t REG_DWORD /d 6 /f >nul 2>&1
reg.exe add "%GAMES%" /v "Scheduling Category" /t REG_SZ /d "High" /f >nul 2>&1
reg.exe add "%GAMES%" /v "SFIO Priority" /t REG_SZ /d "High" /f >nul 2>&1
exit /b 0

:restore
reg.exe add "%SYS%" /v SystemResponsiveness /t REG_DWORD /d 20 /f >nul 2>&1
reg.exe add "%SYS%" /v NetworkThrottlingIndex /t REG_DWORD /d 10 /f >nul 2>&1
reg.exe add "%GAMES%" /v "GPU Priority" /t REG_DWORD /d 8 /f >nul 2>&1
reg.exe add "%GAMES%" /v "Priority" /t REG_DWORD /d 2 /f >nul 2>&1
reg.exe add "%GAMES%" /v "Scheduling Category" /t REG_SZ /d "Medium" /f >nul 2>&1
reg.exe add "%GAMES%" /v "SFIO Priority" /t REG_SZ /d "Normal" /f >nul 2>&1
exit /b 0
