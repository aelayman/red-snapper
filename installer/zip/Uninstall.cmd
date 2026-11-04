@echo off
title Remove Red Snapper
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Uninstall
echo.
pause
