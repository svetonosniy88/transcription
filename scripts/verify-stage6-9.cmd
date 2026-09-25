@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\verify-stage6-9.ps1"
set "RC=%ERRORLEVEL%"
echo.
if "%RC%"=="0" (
  echo PASS: WhisperMd stage 6-9 verification completed.
) else (
  echo FAIL: verification exited with code %RC%.
)
echo.
pause
exit /b %RC%
