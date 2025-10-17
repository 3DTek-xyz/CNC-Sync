@echo off
echo FRESH BUILD SCRIPT
echo ==================
echo Location: %CD%

if not exist "CNCFTPSync.sln" (
    echo FAIL: No solution file
    pause
    exit /b 1
)
echo OK: Solution found
echo.

echo Step 1: Clean
echo Cleaning all bin and obj folders...
if exist "bin" (
    echo   Removing root bin folder
    rmdir /s /q "bin" || echo   - Error removing bin folder
)
if exist "obj" (
    echo   Removing root obj folder
    rmdir /s /q "obj" || echo   - Error removing obj folder
)
echo Cleaning project-specific folders...
for /d %%d in (src\*) do (
    if exist "%%d\bin" (
        echo   Removing %%d\bin
        rmdir /s /q "%%d\bin" || echo   - Error removing %%d\bin
    )
    if exist "%%d\obj" (
        echo   Removing %%d\obj
        rmdir /s /q "%%d\obj" || echo   - Error removing %%d\obj
    )
)
echo Clean done
echo.

echo Step 2: Check dotnet
dotnet --version
if %errorLevel% neq 0 (
    echo FAIL: dotnet not working
    pause
    exit /b 1
)
echo OK: dotnet working
echo.

echo Step 3: Restore
dotnet restore CNCFTPSync.sln
if %errorLevel% neq 0 (
    echo FAIL: restore
    pause
    exit /b 1
)
echo OK: restore done
echo.

echo Step 4: Build solution
echo Building with detailed logging...
dotnet build CNCFTPSync.sln -c Release --no-restore --verbosity minimal
echo Build exit code: %errorLevel%
if %errorLevel% neq 0 (
    echo FAIL: build
    pause
    exit /b 1
)
echo OK: build done
echo.



echo Step 4.1: Check build outputs
echo Checking GUI build output:
if exist "src\CNCFTPSyncGUI\bin\x64\Release\net9.0-windows" (
    echo   GUI x64 build folder exists
    if exist "src\CNCFTPSyncGUI\bin\x64\Release\net9.0-windows\CNCFTPSyncGUI.exe" (
        echo   GUI EXE found
    ) else (
        echo   WARNING: GUI EXE not found
    )
    if exist "src\CNCFTPSyncGUI\bin\x64\Release\net9.0-windows\win-x64" (
        echo   GUI win-x64 subfolder exists
        dir /b "src\CNCFTPSyncGUI\bin\x64\Release\net9.0-windows\win-x64" | find "CNCFTPSyncGUI.exe" >nul
        if %errorLevel% equ 0 (
            echo   GUI EXE found in win-x64 subfolder
        ) else (
            echo   WARNING: GUI EXE not found in win-x64 subfolder
        )
    ) else (
        echo   WARNING: GUI win-x64 subfolder not found
    )
) else (
    echo   WARNING: GUI build folder not found
)
echo.

echo Step 5: Build installer
echo Building installer with detailed logging...
dotnet build src\CNCFTPSync.Installer\CNCFTPSync.Installer.wixproj -c Release --no-restore --verbosity minimal
echo Installer build exit code: %errorLevel%
if %errorLevel% neq 0 (
    echo FAIL: installer
    echo Checking if source files exist for WiX:
    if exist "src\CNCFTPSyncGUI\bin\x64\Release\net9.0-windows\win-x64\CNCFTPSyncGUI.exe" (
        echo   Source GUI EXE exists
    ) else (
        echo   ERROR: Source GUI EXE missing - WiX cannot find source files
    )
    pause
    exit /b 1
)
echo OK: installer done
echo.

echo Step 6: Check outputs
if exist "bin\Release\CNCFTPSync.Installer.msi" (
    echo SUCCESS: MSI created at bin\Release\CNCFTPSync.Installer.msi
    echo.
    echo Step 7: Auto-installing after successful build (force install)
    echo Running installer with force flags...
    msiexec /i "bin\Release\CNCFTPSync.Installer.msi" /l*v install.log /qb REINSTALL=ALL REINSTALLMODE=vamus
    if %errorLevel% equ 0 (
        echo OK: Installation completed successfully
    ) else (
        echo WARNING: Installation may have failed (exit code %errorLevel%)
        echo Check install.log for details
    )
) else (
    echo WARNING: MSI not found - cannot install
)
echo.

echo COMPLETE BUILD SUCCESS!
pause