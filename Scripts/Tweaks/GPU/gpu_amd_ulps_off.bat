@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
call :set_ulps 0
exit /b 0

:restore
call :set_ulps 1
exit /b 0

:set_ulps
rem EnableUlps = 0 -> AMD Ultra Low Power State off (no downclock stutter in games)
set "BASE=HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
for /f "tokens=*" %%K in ('reg.exe query "%BASE%" /f "000" /k 2^>nul ^| findstr /b /i "HKEY"') do (
    reg.exe query "%%K" /v EnableUlps >nul 2>&1
    if not errorlevel 1 reg.exe add "%%K" /v EnableUlps /t REG_DWORD /d %1 /f >nul 2>&1
)
exit /b 0
