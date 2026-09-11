@echo off
setlocal
net session >nul 2>&1
if errorlevel 1 (
  echo Administrator access is required to stop the installed watchdog task cleanly.
  echo Right-click this file and choose "Run as administrator".
  pause
  exit /b 1
)
echo Stopping RTSPView controller and viewer...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Stop-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue; Get-Process -Name 'SpotMonitor.Controller' -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 500; Get-Process -Name 'SpotMonitor.Viewer' -ErrorAction SilentlyContinue | Stop-Process -Force"
if errorlevel 1 (
  echo Could not stop RTSPView.
  pause
  exit /b 1
)
echo RTSPView will remain closed until manually started or the next Windows logon.
pause
endlocal
