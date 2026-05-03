@echo off
setlocal

set "SCRIPT=%~dp0fuse_converter.py"

if "%~1"=="" (
    echo Drag a legacy Railroader mod folder, zip file, or JSON file onto this file.
    echo.
    echo You can also run:
    echo   ConvertToFUSE.cmd "C:\Path\To\LegacyMod" --out "C:\Steam\steamapps\common\Railroader\Mods" --clean
    echo.
    pause
    exit /b 1
)

where py >nul 2>nul
if %ERRORLEVEL%==0 (
    py -3 "%SCRIPT%" %*
) else (
    python "%SCRIPT%" %*
)

echo.
pause
