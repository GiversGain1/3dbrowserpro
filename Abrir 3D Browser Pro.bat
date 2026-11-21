@echo off
setlocal
set "EXE=%~dp0build\portable\3D Browser Pro.exe"

if exist "%EXE%" (
    start "" "%EXE%" %*
    exit /b 0
)

echo Compile primeiro com tools\build-browser.bat.
exit /b 1
