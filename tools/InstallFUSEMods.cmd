@echo off
setlocal

set "SCRIPT=%~dp0fuse_installer.py"

if "%~1"=="" (
    echo Drop mod zip files in the base folder and run this file, or drag zip files onto it.
    echo.
)

where python >nul 2>nul
if %ERRORLEVEL%==0 (
    python "%SCRIPT%" --pause %*
    exit /b
)

py -3 "%SCRIPT%" --pause %*
