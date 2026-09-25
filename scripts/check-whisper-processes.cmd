@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\check-whisper-processes.ps1"
echo.
pause
exit /b %ERRORLEVEL%
