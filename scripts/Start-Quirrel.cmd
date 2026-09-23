@echo off
setlocal
set "config=%~dp0..\drives_config.json"
if not "%~1"=="" set "config=%~f1"
"%~dp0..\QuirrelBasic.exe" run "%config%"
if errorlevel 1 pause
