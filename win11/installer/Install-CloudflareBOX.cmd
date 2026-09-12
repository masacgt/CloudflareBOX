@echo off
setlocal
set "CLOUDFLAREBOX_INSTALL_SCRIPT=%~dp0install.ps1"
set "CLOUDFLAREBOX_INSTALLER_DIR=%~dp0"
set "CLOUDFLAREBOX_INSTALL_LOG=%TEMP%\CloudflareBOX-install-error.log"
del /q "%CLOUDFLAREBOX_INSTALL_LOG%" >nul 2>&1
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$script = [ScriptBlock]::Create((Get-Content -Raw -Encoding UTF8 -LiteralPath $env:CLOUDFLAREBOX_INSTALL_SCRIPT)); & $script -InstallerRoot $env:CLOUDFLAREBOX_INSTALLER_DIR"
set "exitcode=%ERRORLEVEL%"
if not "%exitcode%"=="0" (
  echo.
  echo CloudflareBOX installation failed. See the message above.
  if exist "%CLOUDFLAREBOX_INSTALL_LOG%" (
    echo.
    echo Detailed installer log: %CLOUDFLAREBOX_INSTALL_LOG%
    type "%CLOUDFLAREBOX_INSTALL_LOG%"
  )
  pause
)
exit /b %exitcode%
