@echo off
setlocal
cd /d "%~dp0"

where dotnet.exe >nul 2>&1
if errorlevel 1 (
  echo ERROR: The .NET 8 SDK is required.
  echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
)

echo.
echo BUILD COMPLETED SUCCESSFULLY
echo Output: artifacts\publish\win-x64\NetPulse Monitor.exe
pause
exit /b 0
