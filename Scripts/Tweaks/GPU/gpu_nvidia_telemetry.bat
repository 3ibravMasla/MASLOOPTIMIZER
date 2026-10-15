@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
sc.exe stop NvTelemetryContainer >nul 2>&1
sc.exe config NvTelemetryContainer start= disabled >nul 2>&1
schtasks.exe /Change /TN "NvTmMon" /Disable >nul 2>&1
schtasks.exe /Change /TN "NvTmRep" /Disable >nul 2>&1
schtasks.exe /Change /TN "NvTmRepOnLogon" /Disable >nul 2>&1
reg.exe add "HKLM\SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client" /v OptInOrOutPreference /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "HKLM\SOFTWARE\NVIDIA Corporation\Global\FTS" /v EnableRID66610 /t REG_DWORD /d 0 /f >nul 2>&1
reg.exe add "HKLM\SOFTWARE\NVIDIA Corporation\Global\FTS" /v EnableRID64640 /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0

:restore
sc.exe config NvTelemetryContainer start= auto >nul 2>&1
sc.exe start NvTelemetryContainer >nul 2>&1
schtasks.exe /Change /TN "NvTmMon" /Enable >nul 2>&1
schtasks.exe /Change /TN "NvTmRep" /Enable >nul 2>&1
schtasks.exe /Change /TN "NvTmRepOnLogon" /Enable >nul 2>&1
reg.exe add "HKLM\SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client" /v OptInOrOutPreference /t REG_DWORD /d 1 /f >nul 2>&1
reg.exe delete "HKLM\SOFTWARE\NVIDIA Corporation\Global\FTS" /v EnableRID66610 /f >nul 2>&1
reg.exe delete "HKLM\SOFTWARE\NVIDIA Corporation\Global\FTS" /v EnableRID64640 /f >nul 2>&1
exit /b 0
