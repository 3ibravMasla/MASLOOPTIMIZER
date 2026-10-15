@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Unhide the "Processor performance core parking min cores" setting
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583" /v Attributes /t REG_DWORD /d 0 /f >nul 2>&1
rem ValueMax = 0 -> no parked cores allowed
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583" /v ValueMax /t REG_DWORD /d 0 /f >nul 2>&1
rem Min 100%% of cores must stay unparked (AC + DC) in the active scheme
powercfg.exe -setacvalueindex scheme_current sub_processor CPMINCORES 100 >nul 2>&1
powercfg.exe -setdcvalueindex scheme_current sub_processor CPMINCORES 100 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
exit /b 0

:restore
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583" /v ValueMax /t REG_DWORD /d 100 /f >nul 2>&1
powercfg.exe -setacvalueindex scheme_current sub_processor CPMINCORES 5 >nul 2>&1
powercfg.exe -setdcvalueindex scheme_current sub_processor CPMINCORES 5 >nul 2>&1
powercfg.exe -setactive scheme_current >nul 2>&1
exit /b 0
