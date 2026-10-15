@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
call :scan "apply"
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v gpu_enable_msi_mode /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
call :scan "restore"
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v gpu_enable_msi_mode /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:scan
rem Walk every PCI device instance and touch only Display adapters (ClassGUID match)
set "ENUM=HKLM\SYSTEM\CurrentControlSet\Enum\PCI"
for /f "tokens=*" %%V in ('reg.exe query "%ENUM%" 2^>nul ^| findstr /b /i "HKEY"') do call :vendor "%%V" "%~1"
exit /b 0

:vendor
for /f "tokens=*" %%I in ('reg.exe query "%~1" 2^>nul ^| findstr /b /i "HKEY"') do call :instance "%%I" "%~2"
exit /b 0

:instance
set "CG="
for /f "tokens=2,*" %%A in ('reg.exe query "%~1" /v ClassGUID 2^>nul ^| findstr /i "ClassGUID"') do set "CG=%%B"
if /i not "%CG%"=="{4d36e968-e325-11ce-bfc1-08002be10318}" exit /b 0
if /i "%~2"=="apply" (
    reg.exe add "%~1\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties" /v MSISupported /t REG_DWORD /d 1 /f >nul 2>&1
    reg.exe add "%~1\Device Parameters\Interrupt Management\Affinity Policy" /v DevicePriority /t REG_DWORD /d 3 /f >nul 2>&1
) else (
    reg.exe delete "%~1\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties" /v MSISupported /f >nul 2>&1
    reg.exe delete "%~1\Device Parameters\Interrupt Management\Affinity Policy" /v DevicePriority /f >nul 2>&1
)
exit /b 0
