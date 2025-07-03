@echo off
echo AGI PDM Server Migration Tool
echo =============================
echo.
echo This tool must be run as Administrator.
echo.

:: Check for admin rights
net session >nul 2>&1
if %errorLevel% == 0 (
    echo Running with Administrator privileges...
    cd /d "%~dp0"
    AGI-PDM\bin\Release\net8.0-windows\AGI-PDM.exe
) else (
    echo Requesting Administrator privileges...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
)

pause