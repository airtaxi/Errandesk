@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stage-client-update.ps1" %*
exit /b %ERRORLEVEL%
