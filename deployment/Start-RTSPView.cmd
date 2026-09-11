@echo off
setlocal
start "RTSPView Controller" /min "%~dp0Controller\SpotMonitor.Controller.exe"
timeout /t 2 /nobreak >nul
start "RTSPView Viewer" "%~dp0Viewer\SpotMonitor.Viewer.exe"
endlocal
