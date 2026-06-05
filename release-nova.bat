@echo off
REM Double-click to cut a NOVA (-nova prerelease) release for what's pushed on nova.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -Nova
pause
