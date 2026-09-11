@echo off
setlocal
echo Configuring the private-LAN firewall rule. Run this shortcut as administrator.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Controller\Enable-LanAccess.ps1"
if errorlevel 1 (
  echo Could not configure the rule. Use administrator rights and set your trusted network profile to Private.
) else (
  echo Firewall configured. Enable LAN access on the System tab to open the admin panel to your LAN.
)
pause
endlocal
