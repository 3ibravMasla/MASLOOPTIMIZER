@echo off
setlocal
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Game-oriented TCP stack: RSS on, RSC off, ECN off, faster retransmits
netsh.exe int tcp set global rss=enabled >nul 2>&1
netsh.exe int tcp set global rsc=disabled >nul 2>&1
netsh.exe int tcp set global ecncapability=disabled >nul 2>&1
netsh.exe int tcp set global timestamps=disabled >nul 2>&1
netsh.exe int tcp set global initialRto=2000 >nul 2>&1
netsh.exe int tcp set global maxsynretransmissions=2 >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v NetGamingStack /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0

:restore
netsh.exe int tcp set global rss=default >nul 2>&1
netsh.exe int tcp set global rsc=default >nul 2>&1
netsh.exe int tcp set global ecncapability=default >nul 2>&1
netsh.exe int tcp set global timestamps=default >nul 2>&1
netsh.exe int tcp set global initialRto=3000 >nul 2>&1
netsh.exe int tcp set global maxsynretransmissions=4 >nul 2>&1
reg.exe add "HKCU\Software\MASLOOPTIMIZER\State" /v NetGamingStack /t REG_DWORD /d 0 /f >nul 2>&1
exit /b 0
