@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Kill the "Windows Feedback" prompt engine completely
reg.exe add "HKCU\Software\Microsoft\Siuf\Rules" /v NumberOfSIUFInPeriod /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "HKCU\Software\Microsoft\Siuf\Rules" /v PeriodInNanoSeconds /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" /v DoNotShowFeedbackNotifications /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
reg.exe delete "HKCU\Software\Microsoft\Siuf\Rules" /v NumberOfSIUFInPeriod /f >nul 2>&1
reg.exe delete "HKCU\Software\Microsoft\Siuf\Rules" /v PeriodInNanoSeconds /f >nul 2>&1
reg.exe delete "HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" /v DoNotShowFeedbackNotifications /f >nul 2>&1
exit /b 0
