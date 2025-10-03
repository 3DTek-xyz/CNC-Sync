# Windows G-Code Sync Tool

A comprehensive C# Windows application that monitors folders for G-code file changes and automatically processes and uploads them via FTP. This tool replaces the existing PowerShell script with a robust, service-based solution.

## 🏗️ Architecture
- **Windows Service** (`GCodeSyncService`): Background monitoring and processing
- **Taskbar GUI** (`GCodeSyncGUI`): User interface with system tray, status indication, and manual controls
- **Core Library** (`GCodeSyncCore`): Shared functionality between service and GUI

## ✨ Features
- **Smart Folder Monitoring**: Real-time detection of new folders with intelligent file completion detection
- **G-Code Processing**: Automated CYC coordinate updates, file organization by type
- **FTP Integration**: Anonymous FTP upload with error handling and retry logic
- **Comprehensive Logging**: Detailed logging with GUI display and file output
- **Manual Processing**: On-demand folder reprocessing capability
- **System Tray Integration**: Taskbar icon with status indication (Green/Yellow/Red)
- **Flexible Configuration**: JSON-based configuration with GUI editor
- **Service Management**: Install/uninstall as Windows Service for background operation
- **Multiple Operation Modes**: Run as service or standalone GUI application

## 🚀 Quick Start

### 1. Installation
```cmd
# Build the application
build.bat

# Install as Windows Service (run as Administrator)
install_service.bat
```

### 2. Configuration
1. Run `GCodeSyncGUI.exe` as Administrator
2. Configure paths and FTP settings in the Configuration tab
3. Test FTP connection
4. Save configuration

### 3. Usage
- **Service Mode**: Runs automatically in background after installation
- **Standalone Mode**: Use GUI "Start Monitoring" button for manual operation

## 📋 Requirements
- **OS**: Windows 10/11 or Windows Server 2019/2022
- **Runtime**: .NET 6.0 (included with installer)
- **Privileges**: Administrator rights for service installation
- **Network**: Access to FTP server

## 🔄 File Processing Workflow
1. **Monitor** specified folder for new directory creation
2. **Wait** for files to finish writing (configurable delay + stability check)
3. **Analyze** project to find latest revision (R##) CYC files
4. **Organize** files into structured folders:
   - Move .nc files to `NC/` subfolder
   - Move .cyc, .xml, .jpg files to `AutoStickLabel/` subfolder
5. **Process** CYC XML files:
   - Convert negative Y coordinates to positive values
   - Convert files to UTF-8 encoding
6. **Stage** processed files in FTP upload folder
7. **Upload** to FTP server (if enabled)
8. **Log** all steps with detailed status and error information

## 📁 Project Structure
```
CBWSS-Sync/
├── src/
│   ├── GCodeSyncCore/          # Shared library
│   ├── GCodeSyncService/       # Windows Service
│   └── GCodeSyncGUI/          # GUI Application
├── config/                     # Configuration files
├── logs/                      # Log output directory
├── build.bat                  # Build script
├── install_service.bat        # Service installation
├── uninstall_service.bat      # Service removal
├── README.md                  # This file
├── INSTALLATION.md            # Detailed installation guide
└── CONFIGURATION.md           # Configuration reference
```

## 📚 Documentation
- **[Installation Guide](INSTALLATION.md)**: Step-by-step installation instructions
- **[Configuration Reference](CONFIGURATION.md)**: Detailed configuration options
- **Original PowerShell Script**: `ProcessGcodeToFtpFolder-EmilsPC.ps1` (reference)

## 🛠️ Development
Built with:
- C# / .NET 6.0
- Windows Forms (GUI)
- NLog (Logging)
- System.ServiceProcess (Windows Service)
- FtpWebRequest (FTP Client)

## 🔧 Configuration Example
```json
{
  "watchFolder": "C:\\GCodeWatch",
  "ftpUploadFolder": "C:\\GCodeFtpUpload", 
  "ftpServer": "192.168.1.100",
  "ftpPort": 21,
  "useAnonymousFtp": true,
  "fileStabilityDelaySeconds": 30,
  "autoUploadAfterProcessing": true
}
```

## 🚨 Support & Troubleshooting
- Check log files in `%APPDATA%\GCodeSync\Logs\`
- Use GUI Test FTP Connection feature
- Verify folder permissions and network access
- Review Windows Event Log for service issues

## 📝 License
Copyright © 2025. Ben Harper 3DTek.