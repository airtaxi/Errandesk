@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-signing.ps1" %*
exit /b %ERRORLEVEL%
