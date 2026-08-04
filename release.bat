@echo off
title Bandroom Release
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
pause
