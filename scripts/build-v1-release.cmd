@echo off
setlocal
cd /d "%~dp0.."

echo [1/2] Regression verification...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\verify-stage6-9.ps1"
if errorlevel 1 goto :failed

echo.
echo [2/2] Building v1 installer...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-installer.ps1"
if errorlevel 1 goto :failed

echo.
echo PASS: WhisperMd v1 installer built.
explorer.exe /select,"%CD%\release\WhisperMd-Setup-1.0.0.exe"
echo.
pause
exit /b 0

:failed
set CODE=%ERRORLEVEL%
echo.
echo FAILED with exit code %CODE%.
echo.
pause
exit /b %CODE%
