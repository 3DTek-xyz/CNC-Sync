@echo off
echo ======================================
echo CBWSS-Sync Complete Cache Clean
echo ======================================
echo.

echo Removing all build directories...

if exist "bin" (
    echo Removing bin...
    rmdir /s /q "bin"
)

if exist "obj" (
    echo Removing obj...
    rmdir /s /q "obj"
)

if exist "src\GCodeSyncCore\bin" (
    echo Removing GCodeSyncCore\bin...
    rmdir /s /q "src\GCodeSyncCore\bin"
)

if exist "src\GCodeSyncCore\obj" (
    echo Removing GCodeSyncCore\obj...
    rmdir /s /q "src\GCodeSyncCore\obj"
)

if exist "src\GCodeSyncGUI\bin" (
    echo Removing GCodeSyncGUI\bin...
    rmdir /s /q "src\GCodeSyncGUI\bin"
)

if exist "src\GCodeSyncGUI\obj" (
    echo Removing GCodeSyncGUI\obj...
    rmdir /s /q "src\GCodeSyncGUI\obj"
)

if exist "src\GCodeSyncService\bin" (
    echo Removing GCodeSyncService\bin...
    rmdir /s /q "src\GCodeSyncService\bin"
)

if exist "src\GCodeSyncService\obj" (
    echo Removing GCodeSyncService\obj...
    rmdir /s /q "src\GCodeSyncService\obj"
)

if exist "src\CBWSSSync.Installer\bin" (
    echo Removing CBWSSSync.Installer\bin...
    rmdir /s /q "src\CBWSSSync.Installer\bin"
)

if exist "src\CBWSSSync.Installer\obj" (
    echo Removing CBWSSSync.Installer\obj...
    rmdir /s /q "src\CBWSSSync.Installer\obj"
)

echo.
echo ✅ Complete cache clean finished!
echo.
echo Now run Build-Test-Local.bat for a fresh build
echo.
pause