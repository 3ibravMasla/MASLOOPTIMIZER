@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem memoryusage 2 -> larger NTFS paged pool cache for faster directory/asset streaming
fsutil.exe behavior set memoryusage 2 >nul 2>&1
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v NtfsMemoryUsage /t REG_DWORD /d 2 /f >nul 2>&1
exit /b 0

:restore
fsutil.exe behavior set memoryusage 1 >nul 2>&1
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v NtfsMemoryUsage /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0
