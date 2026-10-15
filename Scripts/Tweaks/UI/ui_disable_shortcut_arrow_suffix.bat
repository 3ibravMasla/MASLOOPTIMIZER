@echo off
setlocal
set "TPL=HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem New shortcuts keep the clean name (no "- Shortcut"/"- Ярлик" suffix)
reg.exe add "%TPL%" /v ShortcutNameTemplate /t REG_SZ /d "%%s.lnk" /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "%TPL%" /v ShortcutNameTemplate /f >nul 2>&1
exit /b 0
