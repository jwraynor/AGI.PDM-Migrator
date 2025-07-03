@echo off
:: AGI PDM Server Migration Tool - Scriptable Version
:: For use in RMM scripts or automation - runs without user interaction
:: Exit codes: 0 = success, 1 = failed, -1 = fatal error

cd /d "%~dp0"

:: Run the migration tool
AGI-PDM\bin\Release\net8.0-windows\AGI-PDM.exe
exit /b %errorlevel%