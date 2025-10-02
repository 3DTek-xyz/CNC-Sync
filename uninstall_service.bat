@echo off
echo Uninstalling G-Code Sync Service...
echo ==================================

REM Check if running as administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator
    echo Right-click on this file and select "Run as administrator"
    pause
    exit /b 1
)

REM Check if service exists
sc query GCodeSyncService >nul 2>&1
if %errorLevel% neq 0 (
    echo G-Code Sync Service is not installed
    pause
    exit /b 0
)

REM Stop the service
echo Stopping G-Code Sync Service...
sc stop GCodeSyncService

REM Wait for service to stop
timeout /t 5 /nobreak >nul

REM Delete the service
echo Removing G-Code Sync Service...
sc delete GCodeSyncService

if %errorLevel% equ 0 (
    echo.
    echo SUCCESS: G-Code Sync Service has been removed successfully!
    echo.
    echo Note: Configuration files and logs are preserved at:
    echo %APPDATA%\GCodeSync\
    echo.
    echo Delete this folder manually if you want to remove all traces.
) else (
    echo ERROR: Failed to remove service
)

echo.
pause