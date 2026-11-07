@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-TexturePaintReleaseGate.ps1" %*
exit /b %ERRORLEVEL%
