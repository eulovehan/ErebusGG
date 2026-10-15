@echo off
powershell.exe -NoProfile -File "%~dp0open-vscode.ps1"
if errorlevel 1 pause
