@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Disable 8.3 legacy short names (SSD/NVMe metadata + directory overhead)
fsutil.exe behavior set disable8dot3 1 >nul 2>&1
rem 0x80000001 -> last access timestamps recording disabled
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v NtfsDisableLastAccessUpdate /t REG_DWORD /d 2147483649 /f >nul 2>&1
exit /b 0

:restore
fsutil.exe behavior set disable8dot3 0 >nul 2>&1
rem 0x80000000 -> Windows default (user managed, updates enabled)
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v NtfsDisableLastAccessUpdate /t REG_DWORD /d 2147483648 /f >nul 2>&1
exit /b 0
