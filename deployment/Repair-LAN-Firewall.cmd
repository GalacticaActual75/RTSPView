@echo off
setlocal
net session >nul 2>&1
if errorlevel 1 (
  echo Right-click this file and choose "Run as administrator".
  pause
  exit /b 1
)
netsh advfirewall firewall delete rule name="SpotMonitor Web Admin - Private LAN" >nul 2>&1
netsh advfirewall firewall delete rule name="SpotMonitor Web Admin - Block Public" >nul 2>&1
netsh advfirewall firewall delete rule name="SpotMonitor Web Admin - LAN Only" >nul 2>&1
netsh advfirewall firewall add rule name="SpotMonitor Web Admin - LAN Only" dir=in action=allow protocol=TCP localport=5080 remoteip=LocalSubnet profile=any enable=yes
echo.
echo SpotMonitor web administration is allowed from the local subnet only.
pause
endlocal
