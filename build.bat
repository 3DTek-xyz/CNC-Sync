@echo off
echo G-Code Sync Tool - Build Script
echo ================================

REM Add .NET to PATH for this session
set PATH=%PATH%;C:\Program Files\dotnet;C:\Program Files (x86)\dotnet

REM Check if .NET is now available
echo Checking for .NET SDK...
dotnet --version
if errorlevel 1 (
    echo ERROR: .NET SDK not found even after adding to PATH
    echo Please verify .NET 9.0 SDK is installed
    pause
    exit /b 1
)

echo Building solution...

REM Verify solution file exists
if not exist "GCodeSync.sln" (
    echo ERROR: GCodeSync.sln not found in current directory
    echo Current directory: %CD%
    pause
    exit /b 1
)

echo Found solution file: GCodeSync.sln

REM Clean previous builds
echo Cleaning previous builds...
dotnet clean GCodeSync.sln --configuration Release
if errorlevel 1 (
    echo WARNING: Clean operation had issues, continuing...
)

REM Restore packages first to avoid NuGet errors
echo Restoring NuGet packages...
dotnet restore GCodeSync.sln

REM Check if restore was successful
if errorlevel 1 (
    echo ERROR: NuGet restore failed
    pause
    exit /b 1
)

echo NuGet restore completed successfully
REM Brief pause to ensure all packages are fully restored
timeout /t 2 /nobreak >nul

REM Build the solution
echo Building solution in Release mode...
dotnet build GCodeSync.sln --configuration Release --no-restore

REM Also build Debug mode for development 
echo Building Debug mode...
dotnet build GCodeSync.sln --configuration Debug --no-restore

if errorlevel 1 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

REM Publish the applications
echo Publishing applications...

REM Create CBWSS-SYNC directory for Windows deployment (Dropbox shared folder)
if not exist "CBWSS-SYNC" mkdir CBWSS-SYNC
if not exist "CBWSS-SYNC\Service" mkdir CBWSS-SYNC\Service
if not exist "CBWSS-SYNC\GUI" mkdir CBWSS-SYNC\GUI

REM Clear previous builds in CBWSS-SYNC
if exist "CBWSS-SYNC\Service\*" del /Q CBWSS-SYNC\Service\*
if exist "CBWSS-SYNC\GUI\*" del /Q CBWSS-SYNC\GUI\*

REM Publish Windows Service to CBWSS-SYNC
echo Publishing Windows Service to CBWSS-SYNC...
dotnet publish src\GCodeSyncService\GCodeSyncService.csproj --configuration Release --output CBWSS-SYNC\Service --self-contained false --runtime win-x64

REM Publish GUI Application to CBWSS-SYNC
echo Publishing GUI Application to CBWSS-SYNC...
dotnet publish src\GCodeSyncGUI\GCodeSyncGUI.csproj --configuration Release --output CBWSS-SYNC\GUI --self-contained false --runtime win-x64

REM Copy additional files to CBWSS-SYNC
echo Copying documentation and scripts to CBWSS-SYNC...
copy README.md CBWSS-SYNC\ >nul
copy INSTALLATION.md CBWSS-SYNC\ >nul
copy CONFIGURATION.md CBWSS-SYNC\ >nul

echo.
echo Build completed successfully!
echo.
echo Output directories:
echo - Service: publish\Service\
echo - GUI: publish\GUI\
echo - Documentation: publish\
echo.
echo Run install_service.bat as Administrator to install the Windows Service
echo Run GCodeSyncGUI.exe to start the GUI application
echo.
pause