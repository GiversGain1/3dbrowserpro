@echo off
where pwsh >nul 2>nul
if errorlevel 1 goto windowsPowerShell
pwsh -NoProfile -File "%~dp0build.ps1"
exit /b %errorlevel%
:windowsPowerShell
powershell -NoProfile -File "%~dp0build.ps1"
exit /b %errorlevel%
