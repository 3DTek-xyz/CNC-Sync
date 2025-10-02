@echo off
echo Installing G-Code Sync Service...
echo ================================

REM Check if running as administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator
    echo Right-click on this file and select "Run as administrator"
    pause
    exit /b 1
)

REM Get the directory where this script is located
set INSTALL_DIR=%~dp0CBWSS-SYNC\Service

REM Check if service executable exists
if not exist "%INSTALL_DIR%\GCodeSyncService.exe" (
    echo ERROR: GCodeSyncService.exe not found in %INSTALL_DIR%
    echo Please ensure you've built the application first using build.bat
    pause
    exit /b 1
)

REM Stop service if it's already running
sc query GCodeSyncService >nul 2>&1
if %errorLevel% equ 0 (
    echo Stopping existing service...
    sc stop GCodeSyncService
    timeout /t 5 /nobreak >nul
)

REM Delete existing service if it exists
sc query GCodeSyncService >nul 2>&1
if %errorLevel% equ 0 (
    echo Removing existing service...
    sc delete GCodeSyncService
    timeout /t 2 /nobreak >nul
)

REM Install the service
echo Installing service...
sc create GCodeSyncService binPath= "\"%INSTALL_DIR%\GCodeSyncService.exe\"" start= auto DisplayName= "G-Code Sync Service"

if %errorLevel% neq 0 (
    echo ERROR: Failed to install service
    pause
    exit /b 1
)

REM Set service description
sc description GCodeSyncService "Monitors G-Code project folders and automatically processes and uploads files via FTP"

REM Set service failure actions (restart on failure)
sc failure GCodeSyncService reset= 86400 actions= restart/30000/restart/60000/restart/60000

REM Start the service
echo Starting service...
sc start GCodeSyncService

if %errorLevel% neq 0 (
    echo WARNING: Service installed but failed to start
    echo Check Windows Event Log for error details
) else (
    echo.
    echo SUCCESS: G-Code Sync Service installed and started successfully!
)

echo.
echo Service Management:
echo - Start: sc start GCodeSyncService
echo - Stop:  sc stop GCodeSyncService
echo - Status: sc query GCodeSyncService
echo - Remove: sc delete GCodeSyncService
echo.
echo You can also use the Windows Services console (services.msc)
echo to manage the service.
echo.

pause