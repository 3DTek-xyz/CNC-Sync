@echo off
echo TEST SCRIPT - Directory Check
echo Current directory: %CD%
echo Script location: %~dp0

if exist "CNCFTPSync.sln" (
    echo SUCCESS: Found CNCFTPSync.sln
) else (
    echo FAIL: CNCFTPSync.sln not found
)

echo.
echo Contents of current directory:
dir *.sln
echo.
pause