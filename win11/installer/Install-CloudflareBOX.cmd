@echo off
setlocal
powershell.exe -NoProfile -File "%~dp0install.ps1"
set "exitcode=%ERRORLEVEL%"
if not "%exitcode%"=="0" (
  echo.
  echo CloudflareBOX installation failed. See the message above.
  pause
)
exit /b %exitcode%
