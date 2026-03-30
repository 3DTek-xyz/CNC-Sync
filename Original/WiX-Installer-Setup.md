# WiX Installer Setup Instructions

## Prerequisites for Building the MSI Installer

### 1. Install WiX Toolset v5
On Windows development machine:
```powershell
# Install via .NET tool (recommended)
dotnet tool install --global wix

# Or download from: https://wixtoolset.org/releases/
```

### 2. Visual Studio Requirements
- Visual Studio 2022 with .NET 9.0 SDK
- WiX Toolset Visual Studio Extension (optional but helpful)

## Building the Installer

### Command Line Build (Recommended)
```bash
# Build Release version of all projects first
dotnet build --configuration Release

# Build the installer
dotnet build src/CNCSync.Installer/CNCSync.Installer.wixproj --configuration Release
```

### Visual Studio Build
1. Open `GCodeSync.sln` in Visual Studio
2. Set build configuration to **Release**
3. Right-click on `CNCSync.Installer` project
4. Select "Build"

## Installer Output

The MSI installer will be created at:
```
bin/Release/CNCSync.Installer.msi
```

## Installer Features

### What Gets Installed
- **Main Application**: `GCodeSyncGUI.exe` (Windows Forms GUI)
- **Service**: `GCodeSyncService.exe` (Windows Service)  
- **Core Library**: `GCodeSyncCore.dll` + dependencies
- **AutoUpdater**: `AutoUpdater.NET.dll` + WebView2 runtime
- **Configuration**: Sample config files and NLog.config
- **Shortcuts**: Start Menu and Desktop shortcuts

### Installation Locations
- **Program Files**: `C:\Program Files\CBWSS-Sync\`
- **Configuration**: `C:\Program Files\CBWSS-Sync\config\`
- **Logs**: `C:\Program Files\CBWSS-Sync\logs\`
- **Start Menu**: `Start Menu\Programs\CBWSS-Sync\`

### Service Installation
The installer automatically:
1. **Stops** existing service (if running)
2. **Uninstalls** old service (during upgrade)
3. **Installs** new service
4. **Starts** the service

## Customization Options

### Branding Images
Replace these files for custom branding:
- `Banner.bmp` (493x58): Top banner in installer dialogs
- `Dialog.bmp` (493x312): Left side image in welcome/finish dialogs

### License Agreement
Edit `License.rtf` to customize the license agreement text.

### Version Information
Update version in `Package.wxs`:
```xml
<Package Version="1.0.0.0" ... />
```

### Product Information
Customize company and product details in `Package.wxs`:
```xml
<Package Name="CBWSS-Sync"
         Manufacturer="3DTek"
         ... />
```

## Silent Installation (for AutoUpdater)

The installer supports silent installation for AutoUpdater.NET:
```cmd
msiexec /i CNCSync.Installer.msi /quiet /norestart
```

Properties available:
- `/quiet`: Silent installation
- `/passive`: Progress bar only
- `/norestart`: Don't restart computer
- `INSTALLFOLDER="C:\CustomPath"`: Custom installation path

## Uninstallation

### Through Windows
- Control Panel → Programs → Uninstall CBWSS-Sync
- Settings → Apps → CBWSS-Sync → Uninstall

### Command Line
```cmd
msiexec /x CNCSync.Installer.msi /quiet
```

### What Gets Removed
- All installed files and folders
- Windows Service (stopped and uninstalled)
- Start Menu shortcuts
- Desktop shortcuts
- Registry entries

**Note**: User data and custom configuration files are preserved.

## Troubleshooting

### Build Errors
1. **WiX not found**: Install WiX toolset globally
2. **Missing dependencies**: Build Release configuration first
3. **File not found**: Check file paths in Components.wxs

### Installation Errors
1. **Service install fails**: Run installer as Administrator
2. **Permission denied**: Ensure target folder is writable
3. **Missing .NET**: Install .NET 9.0 Runtime

### Service Issues
1. **Service won't start**: Check Windows Event Log
2. **Configuration errors**: Review config.json format
3. **File permissions**: Ensure service account has folder access

## Development Workflow

### Making Changes
1. Update version number in `Package.wxs`
2. Build Release configuration
3. Build installer project
4. Test installation/uninstallation
5. Update `update.xml` with new version info
6. Create GitHub release with new MSI

### Testing Process
1. **Fresh Install**: Test on clean system
2. **Upgrade**: Test over previous version
3. **Uninstall**: Verify clean removal
4. **Service**: Test service start/stop functionality
5. **AutoUpdater**: Test update detection and installation

This installer provides a professional deployment solution for CBWSS-Sync with full Windows integration!