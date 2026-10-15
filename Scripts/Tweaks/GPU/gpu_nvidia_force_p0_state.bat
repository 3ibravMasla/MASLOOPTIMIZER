@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
call :set_pstate 1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v gpu_nvidia_force_p0_state /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
call :set_pstate 0
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v gpu_nvidia_force_p0_state /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:set_pstate
rem DisableDynamicPstate = 1 -> no P-State downclocking in light 3D scenes (NVIDIA only)
set "CLS=HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
for /f "tokens=*" %%K in ('reg.exe query "%CLS%" /f "00" /k 2^>nul ^| findstr /b /i "HKEY"') do call :one "%%K" %1
exit /b 0

:one
reg.exe query "%~1" /v ProviderName 2>nul | findstr /i "NVIDIA" >nul
if errorlevel 1 exit /b 0
reg.exe add "%~1" /v DisableDynamicPstate /t REG_DWORD /d %2 /f >nul 2>&1
exit /b 0
