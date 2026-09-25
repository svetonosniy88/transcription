@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\package-source.ps1"
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%
