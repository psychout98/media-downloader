@echo off
title Media Library Import Tool
echo.
echo ============================================
echo   Media Library Import Tool
echo ============================================
echo.

:: Run the PowerShell script from the same directory as this .bat
powershell -ExecutionPolicy Bypass -File "%~dp0import-media.ps1" %*

echo.
pause
