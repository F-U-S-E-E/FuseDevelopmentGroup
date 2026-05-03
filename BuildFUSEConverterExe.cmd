@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build_converter_exe.ps1" %*

echo.
pause
