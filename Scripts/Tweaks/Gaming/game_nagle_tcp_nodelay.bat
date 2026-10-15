@echo off
setlocal
set "BASE=HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"
if /i "%~1"=="apply" goto :apply
if /i "%~1"=="restore" goto :restore
exit /b 0

:apply
rem Disable Nagle's algorithm on every active interface (lower network latency)
for /f "tokens=*" %%K in ('reg.exe query "%BASE%" /f "{" /k 2^>nul ^| findstr /b /i "HKEY"') do (
    reg.exe add "%%K" /v TcpAckFrequency /t REG_DWORD /d 1 /f >nul 2>&1
    reg.exe add "%%K" /v TCPNoDelay /t REG_DWORD /d 1 /f >nul 2>&1
    reg.exe add "%%K" /v TcpDelAckTicks /t REG_DWORD /d 0 /f >nul 2>&1
)
netsh.exe int tcp set global rss=enabled >nul 2>&1
exit /b 0

:restore
for /f "tokens=*" %%K in ('reg.exe query "%BASE%" /f "{" /k 2^>nul ^| findstr /b /i "HKEY"') do (
    reg.exe delete "%%K" /v TcpAckFrequency /f >nul 2>&1
    reg.exe delete "%%K" /v TCPNoDelay /f >nul 2>&1
    reg.exe delete "%%K" /v TcpDelAckTicks /f >nul 2>&1
)
exit /b 0
