@echo off
REM SimpleFolderCopy.bat
REM Demo Batch script for CNC-FTP-SYNC external processing
REM 
REM This script demonstrates how to create custom processing logic that replaces
REM the built-in G-code file processing when "Use External Script" is enabled
REM in the CNC-FTP-SYNC configuration.
REM
REM PARAMETERS:
REM The CNC-FTP-SYNC system calls this script with 3 parameters:
REM %1 - SourcePath      - Full path to the source folder that was detected by file watcher (e.g., "C:\Watch\NewProject_Rev1")  
REM %2 - FtpUploadPath   - Base FTP upload directory path (e.g., "C:\FTPUpload" or "\\server\upload")
REM %3 - LogFilePath     - Path to the main CNC-FTP-SYNC log file for integrated logging
REM
REM RETURN VALUES:
REM The script must return:
REM 1. Exit Code: 0 for success, non-zero for failure
REM 2. Output Path: The script outputs the full path to the prepared files (via stdout) !!! MUST Start with "Path=" eg "Path=C:\FTPUpload\NewProject_Rev1"
REM
REM USAGE:
REM To use this script:
REM 1. Enable "Use External Script for processing" in CNC-FTP-SYNC configuration
REM 2. Browse and select this script (or your customized version)
REM 3. The script will be called instead of built-in G-code processing
REM
REM CUSTOMIZATION:
REM Copy this file and modify it to implement your specific processing needs:
REM - Custom file filtering and processing
REM - File transformation or validation
REM - Integration with other systems
REM - Custom naming and folder structure
REM - Advanced error handling and logging

setlocal enabledelayedexpansion

REM Get parameters
set "SourcePath=%~1"
set "FtpUploadPath=%~2"
set "LogFilePath=%~3"

REM Remove quotes if present
set SourcePath=%SourcePath:"=%
set FtpUploadPath=%FtpUploadPath:"=%
set LogFilePath=%LogFilePath:"=%

REM Initialize variables
set ErrorCount=0
set SuccessCount=0

REM Start processing
call :WriteLog "=== CNC-FTP-SYNC External Script Processing Started ==="
call :WriteLog "Script: SimpleFolderCopy.bat"
call :WriteLog "SourcePath: %SourcePath%"
call :WriteLog "FtpUploadPath: %FtpUploadPath%"
call :WriteLog "LogFilePath: %LogFilePath%"

REM Validate that source folder exists
if not exist "%SourcePath%" (
    call :WriteLog "ERROR: Source folder does not exist: %SourcePath%" "ERROR"
    exit /b 1
)

REM Validate that FTP upload base path exists
if not exist "%FtpUploadPath%" (
    call :WriteLog "ERROR: FTP upload path does not exist: %FtpUploadPath%" "ERROR"
    exit /b 1
)

REM Extract folder name from source path
for %%i in ("%SourcePath%") do set "SourceFolderName=%%~ni"

REM Create the full destination path within FTP upload directory
set "FullDestinationPath=%FtpUploadPath%\%SourceFolderName%"

call :WriteLog "Source folder name: %SourceFolderName%"
call :WriteLog "Destination folder: %FullDestinationPath%"

REM Create destination directory if it doesn't exist
if not exist "%FullDestinationPath%" (
    mkdir "%FullDestinationPath%" 2>nul
    if !errorlevel! neq 0 (
        call :WriteLog "Failed to create destination directory: %FullDestinationPath%" "ERROR"
        exit /b 1
    )
    call :WriteLog "Created destination directory: %FullDestinationPath%"
)

REM Copy all files preserving folder structure
call :WriteLog "Starting file copy operation..."
xcopy "%SourcePath%\*.*" "%FullDestinationPath%\" /E /I /Y /Q >nul 2>&1

if !errorlevel! neq 0 (
    call :WriteLog "File copy operation failed" "ERROR"
    set /a ErrorCount+=1
) else (
    call :WriteLog "File copy operation completed successfully"
    set /a SuccessCount+=1
)

REM Report results
call :WriteLog "=== Processing Complete ==="
call :WriteLog "Copy operation result: Success=%SuccessCount%, Errors=%ErrorCount%"
call :WriteLog "Prepared files location: %FullDestinationPath%"
call :WriteLog "==============================================="

REM Output the path to prepared files (for service to capture)
if %ErrorCount% equ 0 (
    call :WriteLog "Script completed successfully" "SUCCESS"
    REM Output the destination path to stdout with Path= prefix for easy parsing
    echo Path=%FullDestinationPath%
    exit /b 0
) else (
    call :WriteLog "Script completed with errors" "ERROR"
    exit /b 1
)

REM Function to write log messages with timestamps
:WriteLog
set Message=%~1
set Level=%~2
if "%Level%"=="" set Level=INFO

REM Get current timestamp
for /f "tokens=2 delims==" %%i in ('wmic OS Get localdatetime /value') do set datetime=%%i
set year=%datetime:~0,4%
set month=%datetime:~4,2%
set day=%datetime:~6,2%
set hour=%datetime:~8,2%
set minute=%datetime:~10,2%
set second=%datetime:~12,2%
set timestamp=%year%-%month%-%day% %hour%:%minute%:%second%

set "logEntry=[%timestamp%] [%Level%] [External Script] %Message%"

REM Only write to log file, NOT to stdout - stdout is reserved for return path
if not "%LogFilePath%"=="" (
    echo !logEntry! >> "%LogFilePath%" 2>nul
)

goto :eof