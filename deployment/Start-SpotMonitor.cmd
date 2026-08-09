@echo off
setlocal
start "SpotMonitor Controller" /min "%~dp0Controller\SpotMonitor.Controller.exe"
timeout /t 2 /nobreak >nul
start "SpotMonitor Viewer" "%~dp0Viewer\SpotMonitor.Viewer.exe"
endlocal
