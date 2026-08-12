@echo off
setlocal
cd /d "%~dp0"

if "%~1"=="" (
  echo Usage: SIGN-RELEASE.bat CERTIFICATE_THUMBPRINT [FILE]
  echo The certificate must be in the current Windows user's certificate store.
  exit /b 2
)

set "TARGET=%~2"
if "%TARGET%"=="" set "TARGET=%~dp0artifacts\publish\win-x64\NetPulseMonitor.exe"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign-release.ps1" -Files "%TARGET%" -CertificateThumbprint "%~1"
if errorlevel 1 exit /b 1
exit /b 0
