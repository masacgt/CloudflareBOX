@echo off
setlocal
set "CLOUDFLAREBOX_INSTALL_SCRIPT=%~dp0install.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$script = [ScriptBlock]::Create((Get-Content -Raw -LiteralPath $env:CLOUDFLAREBOX_INSTALL_SCRIPT)); & $script"
set "exitcode=%ERRORLEVEL%"
if not "%exitcode%"=="0" (
  echo.
  echo CloudflareBOX installation failed. See the message above.
  pause
)
exit /b %exitcode%
