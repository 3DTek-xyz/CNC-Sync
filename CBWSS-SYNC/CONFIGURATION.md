# G-Code Sync Tool Configuration

## Overview
The G-Code Sync Tool uses a JSON configuration file stored in the user's AppData folder. The configuration can be edited through the GUI application or by directly modifying the JSON file.

## Configuration File Location
```
C:\ProgramData\CNC-FTP-SYNC\CNC-FTP-SYNC-Config.json
```

## Logging Location
```
C:\ProgramData\CNC-FTP-SYNC\Logs\
```

## Configuration Properties

### Folder Settings
- **WatchFolder**: Path to monitor for new G-Code project folders
- **FtpUploadFolder**: Local staging directory for FTP uploads

### FTP Settings
- **FtpServer**: FTP server hostname or IP address
- **FtpPort**: FTP server port (default: 21)
- **UseAnonymousFtp**: Enable anonymous FTP login (default: true)
- **FtpUsername**: Username for authenticated FTP (if not anonymous)
- **FtpPassword**: Password for authenticated FTP (if not anonymous)

### Processing Settings
- **FileStabilityDelaySeconds**: Time to wait after folder creation before processing (default: 30)
- **FileStabilityCheckIntervalSeconds**: Interval for checking file stability (default: 5)
- **AutoUploadAfterProcessing**: Automatically upload to FTP after processing (default: true)

### Logging Settings
- **LogFilePath**: Path for log files
- **EnableDetailedLogging**: Enable detailed logging (default: true)

## Example Configuration

```json
{
  "watchFolder": "C:\\GCodeWatch",
  "ftpUploadFolder": "C:\\GCodeFtpUpload",
  "ftpServer": "192.168.1.100",
  "ftpPort": 21,
  "useAnonymousFtp": true,
  "ftpUsername": "anonymous",
  "ftpPassword": "anonymous@example.com",
  "fileStabilityDelaySeconds": 30,
  "fileStabilityCheckIntervalSeconds": 5,
  "logFilePath": "C:\\Users\\YourUser\\AppData\\Roaming\\GCodeSync\\Logs\\GCodeSync.log",
  "enableDetailedLogging": true,
  "autoUploadAfterProcessing": true
}
```

## Security Considerations

### FTP Credentials
- Passwords are stored in plain text in the configuration file
- Ensure the configuration directory has appropriate file permissions
- Consider using anonymous FTP if security is a concern
- For production environments, consider using FTPS or SFTP (future enhancement)

### File Permissions
The service account needs:
- Read access to the watch folder
- Write access to the FTP upload folder
- Network access to the FTP server

## Advanced Configuration

### NLog Configuration
Edit `NLog.config` in the application directory to customize logging:

```xml
<targets>
  <target xsi:type="File" name="fileTarget"
          fileName="${var:logDirectory}/GCodeSync-${shortdate}.log"
          maxArchiveFiles="30" />
</targets>
```

### Windows Service Configuration
After installation, you can modify service properties:

```cmd
# Change service startup type
sc config GCodeSyncService start= demand

# Set service dependencies
sc config GCodeSyncService depend= "Tcpip/Afd"

# Configure service recovery options
sc failure GCodeSyncService reset= 86400 actions= restart/30000
```

## Monitoring and Maintenance

### Log Files
- Logs rotate daily and keep 30 days of history
- Check logs for processing errors and FTP issues
- Logs include timestamps, severity levels, and detailed messages

### Performance Tuning
- Adjust `FileStabilityDelaySeconds` based on your file copy times
- Monitor disk space in the FTP upload folder
- Consider network bandwidth for FTP uploads

### Health Checks
The service performs automatic health checks and will attempt to restart monitoring if issues are detected.

## Backup and Recovery

### Backup Configuration
```cmd
copy "%APPDATA%\GCodeSync\GCodeSyncConfig.json" "backup_location\"
```

### Restore Configuration
```cmd
copy "backup_location\GCodeSyncConfig.json" "%APPDATA%\GCodeSync\"
```

### Migration to New Machine
1. Copy the entire `%APPDATA%\GCodeSync\` folder
2. Ensure .NET 6.0 Runtime is installed
3. Install and configure the service
4. Verify folder paths are accessible on the new machine