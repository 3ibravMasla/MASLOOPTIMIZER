@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem USB selective suspend = 0 (Off) so mice/keyboards/headsets never drop connection
powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0 >nul 2>&1
powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0 >nul 2>&1
powercfg.exe /setactive SCHEME_CURRENT >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v UsbSuspendOff /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 1 >nul 2>&1
powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 1 >nul 2>&1
powercfg.exe /setactive SCHEME_CURRENT >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v UsbSuspendOff /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
