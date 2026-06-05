@echo off
REM Double-click to cut a STABLE (-torii) release for what's pushed on master.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
pause
