@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Keep the DirectX shader cache from being evicted (0 = no artificial limit)
reg.exe add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DirectX\ShaderCache" /v ShaderCacheLimit /t REG_DWORD /d 0 /f >nul 2>&1
rem NVIDIA GL shader cache policy (only when an NVIDIA driver is present)
reg.exe query "HKLM\SOFTWARE\NVIDIA Corporation" >nul 2>&1
if not errorlevel 1 reg.exe add "HKLM\SOFTWARE\NVIDIA Corporation\Global\NvCache" /v GLCache /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v ShaderCacheBoost /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DirectX\ShaderCache" /v ShaderCacheLimit /f >nul 2>&1
reg.exe query "HKLM\SOFTWARE\NVIDIA Corporation" >nul 2>&1
if not errorlevel 1 reg.exe delete "HKLM\SOFTWARE\NVIDIA Corporation\Global\NvCache" /v GLCache /f >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v ShaderCacheBoost /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
