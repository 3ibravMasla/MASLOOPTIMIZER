@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
call :scan 0
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v UsbPowerSaveOff /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
call :scan 1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v UsbPowerSaveOff /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:scan
rem 0 = power saving off for USB hubs/HID devices (no idle D3, no enhanced power management)
for /f "tokens=*" %%K in ('reg.exe query "HKLM\SYSTEM\CurrentControlSet\Enum\USB" /s /v EnhancedPowerManagementEnabled 2^>nul ^| findstr /b /i "HKEY"') do (
    reg.exe add "%%K" /v EnhancedPowerManagementEnabled /t REG_DWORD /d %1 /f >nul 2>&1
    reg.exe add "%%K" /v AllowIdleIrpInD3 /t REG_DWORD /d %1 /f >nul 2>&1
)
exit /b 0
