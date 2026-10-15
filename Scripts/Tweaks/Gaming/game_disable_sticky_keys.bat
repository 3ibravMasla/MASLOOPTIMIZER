@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem 506 = StickyKeys off (no 5x Shift popup)
reg.exe add "HKCU\Control Panel\Accessibility\StickyKeys" /v Flags /t REG_SZ /d "506" /f >nul 2>&1
rem 122 = filter keys off, no keyboard shortcut
reg.exe add "HKCU\Control Panel\Accessibility\Keyboard Response" /v Flags /t REG_SZ /d "122" /f >nul 2>&1
rem 58 = toggle keys off
reg.exe add "HKCU\Control Panel\Accessibility\ToggleKeys" /v Flags /t REG_SZ /d "58" /f >nul 2>&1
exit /b 0

:restore
reg.exe add "HKCU\Control Panel\Accessibility\StickyKeys" /v Flags /t REG_SZ /d "510" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Accessibility\Keyboard Response" /v Flags /t REG_SZ /d "126" /f >nul 2>&1
reg.exe add "HKCU\Control Panel\Accessibility\ToggleKeys" /v Flags /t REG_SZ /d "62" /f >nul 2>&1
exit /b 0
