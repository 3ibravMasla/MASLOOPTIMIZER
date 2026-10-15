@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
call :scan "apply"
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v gpu_amd_disable_stutter_features /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
call :scan "restore"
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v gpu_amd_disable_stutter_features /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:scan
rem AMD power-saving stutter switches on every AMD display adapter key
set "CLS=HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
for /f "tokens=*" %%K in ('reg.exe query "%CLS%" /f "00" /k 2^>nul ^| findstr /b /i "HKEY"') do call :one "%%K" "%~1"
exit /b 0

:one
reg.exe query "%~1" /v ProviderName 2>nul | findstr /i "AMD ATI Advanced" >nul
if errorlevel 1 exit /b 0
if /i "%~2"=="apply" (
    reg.exe add "%~1" /v EnableAspmL0s /t REG_DWORD /d 0 /f >nul 2>&1
    reg.exe add "%~1" /v EnableAspmL1 /t REG_DWORD /d 0 /f >nul 2>&1
    reg.exe add "%~1" /v PP_ThermalAutoThrottlingEnable /t REG_DWORD /d 0 /f >nul 2>&1
    reg.exe add "%~1" /v DisableDMACopy /t REG_DWORD /d 1 /f >nul 2>&1
    reg.exe add "%~1" /v DisableBlockWrite /t REG_DWORD /d 0 /f >nul 2>&1
) else (
    reg.exe add "%~1" /v EnableAspmL0s /t REG_DWORD /d 1 /f >nul 2>&1
    reg.exe add "%~1" /v EnableAspmL1 /t REG_DWORD /d 1 /f >nul 2>&1
    reg.exe add "%~1" /v PP_ThermalAutoThrottlingEnable /t REG_DWORD /d 1 /f >nul 2>&1
    reg.exe delete "%~1" /v DisableDMACopy /f >nul 2>&1
    reg.exe delete "%~1" /v DisableBlockWrite /f >nul 2>&1
)
exit /b 0
