@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-workspace.ps1"
if errorlevel 1 (
  echo.
  echo CLEANUP FAILED
  pause
  exit /b 1
)
echo.
echo CLEANUP COMPLETED
pause
