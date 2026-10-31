@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restore.ps1"
set "CODE=%ERRORLEVEL%"
echo.
if not "%CODE%"=="0" echo ERROR: exit code %CODE%
pause
exit /b %CODE%
