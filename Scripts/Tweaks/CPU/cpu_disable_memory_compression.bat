@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem RAM memory compression costs CPU cycles - useless on 16 GB+ systems
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Disable-MMAgent -MemoryCompression" >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v MemCompressionOff /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Enable-MMAgent -MemoryCompression" >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v MemCompressionOff /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
