@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem 0x04000000 = 67108864 KB (64 GB): services stop being split into ~80 svchost.exe
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control" /v SvcHostSplitThresholdInKB /t REG_DWORD /d 67108864 /f >nul 2>&1
exit /b 0

:restore
rem 0x00380000 = 3670016 KB (3.5 GB) - Windows default
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control" /v SvcHostSplitThresholdInKB /t REG_DWORD /d 3670016 /f >nul 2>&1
exit /b 0
