# G-Code Sync Tool - Installation Guide

## System Requirements
- Windows 10/11 or Windows Server 2019/2022
- .NET 6.0 Runtime (will be installed automatically)
- Administrator privileges for service installation
- Network access to FTP server

## Installation Steps

### 1. Download and Extract
1. Download the latest release from the releases page
2. Extract the ZIP file to a permanent location (e.g., `C:\Program Files\CNC-FTP-SYNC\`)

### 2. Install as Windows Service (Recommended)

Open Command Prompt as Administrator and navigate to the installation folder:

```cmd
cd "C:\Program Files\CNC-FTP-SYNC"

# Install the service
sc create "CNCFTPSyncService" binPath= "C:\Program Files\CNC-FTP-SYNC\CNCFTPSyncService.exe" start= auto

# Set service description
sc description "CNCFTPSyncService" "CNC file monitoring and FTP synchronization service"

# Start the service
sc start CNCFTPSyncService
```

### 3. Configure the Application

1. Run `CNCFTPSyncGUI.exe` as Administrator (first time only)
2. Go to the Configuration tab
3. Set up your paths and FTP settings:
   - **Watch Folder**: Folder to monitor for new G-Code projects
   - **FTP Upload Folder**: Local staging folder for FTP uploads
   - **FTP Server**: Your FTP server address
   - **FTP Port**: Usually 21 for standard FTP
   - **Anonymous FTP**: Check if your server allows anonymous access

4. Click "Save Configuration"
5. Click "Test FTP Connection" to verify settings

### 4. Start Monitoring

**Option A: Windows Service (Background)**
- The service will start automatically after installation
- Monitor via GUI or Windows Services console

**Option B: Standalone Mode (GUI)**
- Use the GUI application's "Start Monitoring" button
- Runs only while GUI is open

## Configuration File Location

The configuration is stored at:
```
C:\ProgramData\CNC-FTP-SYNC\CNC-FTP-SYNC-Config.json
```

## Logging Location

All logs (GUI and Service) are stored at:
```
C:\ProgramData\CNC-FTP-SYNC\Logs\
```

## Default Folder Structure

The application creates these folders automatically:
```
Watch Folder/           # Your configured watch folder
└── ProjectName/        # New project folders appear here
    ├── *.cyc          # CYC files (processed)
    ├── *.nc           # NC files (moved to NC subfolder)
    ├── *.xml          # XML files (moved to AutoStickLabel)
    ├── *.jpg          # JPG files (moved to AutoStickLabel)
    └── [Generated folders]
        ├── NC/        # Processed NC files
        └── AutoStickLabel/  # Processed CYC, XML, JPG files

FTP Upload Folder/      # Your configured FTP staging folder
└── ProjectName-R##/    # Generated folders ready for FTP
    ├── NC/
    └── AutoStickLabel/
```

## Uninstallation

### Remove Windows Service
```cmd
# Stop the service
sc stop CNCFTPSyncService

# Delete the service
sc delete CNCFTPSyncService
```

### Remove Files
1. Delete the installation folder
2. Delete configuration folder: `%APPDATA%\CNC-FTP-SYNC\`

## Troubleshooting

### Service Won't Start
1. Check Windows Event Log (Application section)
2. Verify .NET 6.0 Runtime is installed
3. Check folder permissions
4. Review logs in `%APPDATA%\CNC-FTP-SYNC\Logs\`

### Files Not Processing
1. Check watch folder permissions
2. Verify file stability delay settings
3. Ensure CYC files contain revision numbers (R##)
4. Check logs for specific error messages

### FTP Upload Failures
1. Test FTP connection using GUI
2. Verify firewall settings
3. Check FTP server accessibility
4. Review anonymous vs. authenticated access requirements

## Advanced Configuration

### Custom File Stability Settings
- **File Stability Delay**: Time to wait after folder creation (default: 30 seconds)
- **Check Interval**: How often to check file stability (default: 5 seconds)

### Logging Configuration
Edit `NLog.config` to customize logging levels and targets.

## Support
- Check log files for detailed error information
- Ensure all file paths use backslashes on Windows
- Run GUI as Administrator for initial setup

## File Processing Details

The application processes G-Code projects with this workflow:
1. Monitors for new folder creation
2. Waits for file writing to complete
3. Finds latest revision (R##) in CYC filenames
4. Organizes files into NC and AutoStickLabel folders
5. Processes CYC XML coordinates (converts negative Y values to positive)
6. Converts CYC files to UTF-8 encoding
7. Copies organized folders to FTP staging area
8. Uploads to FTP server (if configured)
9. Logs all steps for monitoring and debugging