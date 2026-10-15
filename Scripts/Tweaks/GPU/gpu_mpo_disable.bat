@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem OverlayTestMode = 5 -> Multi-Plane Overlay disabled (fixes stutter/black screens with NVIDIA/AMD)
reg.exe add "HKLM\SOFTWARE\Microsoft\Windows\Dwm" /v OverlayTestMode /t REG_DWORD /d 5 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "HKLM\SOFTWARE\Microsoft\Windows\Dwm" /v OverlayTestMode /f >nul 2>&1
exit /b 0
