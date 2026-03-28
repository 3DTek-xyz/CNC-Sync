# AutoUpdater.NET Setup Guide for CBWSS-Sync

## Overview
CBWSS-Sync now includes automatic update functionality using AutoUpdater.NET. This guide explains how to set up the update server and deployment process.

## Update Server Setup

### 1. XML Manifest File (update.xml)
Create and host an XML file with update information:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>1.1.0.0</version>
    <url>https://your-server.com/CBWSS-Sync/releases/CBWSS-Sync-Setup-v1.1.0.exe</url>
    <changelog>https://your-server.com/CBWSS-Sync/releases/changelog.html</changelog>
    <mandatory>false</mandatory>
    <args>/SILENT</args>
    <checksum algorithm="SHA512">ACTUAL_FILE_CHECKSUM_HERE</checksum>
</item>
```

### 2. Update Server Configuration
1. **Host the update.xml file** at a publicly accessible URL
2. **Update the URL in MainForm.cs** - Replace `"https://your-server.com/CBWSS-Sync/update.xml"` with your actual URL
3. **Host the installer files** at the URLs specified in the XML

### 3. Version Management
- Update the `<version>` in update.xml when releasing new versions
- Ensure the version follows the format: `Major.Minor.Build.Revision` (e.g., "1.1.0.0")
- The application will compare this with its assembly version

### 4. Installer Requirements
- Create an installer (MSI or EXE) for your application
- The installer should support silent installation via command-line arguments
- Common arguments: `/SILENT`, `/VERYSILENT`, `/S`, `/quiet`

## Update Process Flow

1. **Application Startup**: Automatically checks for updates
2. **Manual Check**: Users can check via File menu or system tray
3. **Service Handling**: Automatically stops Windows service before updating
4. **Download & Install**: Downloads and runs the installer
5. **Application Restart**: Restarts after update completion

## Security Features

### Checksum Verification
- Generate SHA512 checksum of your installer file
- Add it to the XML manifest for file integrity verification
- AutoUpdater.NET will verify the downloaded file before installation

### HTTPS Recommended
- Use HTTPS URLs for both update.xml and installer downloads
- Protects against man-in-the-middle attacks

## Deployment Workflow

1. **Build Release Version**
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained
   ```

2. **Create Installer**
   - Package the published files into an installer
   - Test the installer with silent installation

3. **Generate Checksum**
   ```bash
   certutil -hashfile CBWSS-Sync-Setup.exe SHA512
   ```

4. **Update XML Manifest**
   - Increment version number
   - Update download URL
   - Update checksum value

5. **Deploy to Server**
   - Upload installer to download location
   - Upload updated update.xml to server

## Configuration Options

### AutoUpdater Settings (in MainForm.cs)
```csharp
AutoUpdater.ShowSkipButton = true;          // Allow users to skip updates
AutoUpdater.ShowRemindLaterButton = true;   // Allow remind later option
AutoUpdater.RemindLaterTimeSpan = RemindLaterFormat.Hours;
AutoUpdater.RemindLaterAt = 24;             // Check again in 24 hours
```

### Custom Update Behavior
- Modify `AutoUpdater_CheckForUpdateEvent` to customize update dialogs
- Adjust `AutoUpdater_ApplicationExitEvent` to handle service shutdown

## Testing Updates

### Local Testing
1. Create a local web server or use GitHub Pages
2. Host test update.xml with higher version number
3. Point application to test URL
4. Verify update detection and download process

### Production Testing
1. Test with beta users first
2. Monitor update success rates
3. Have rollback plan ready

## Troubleshooting

### Common Issues
- **No updates detected**: Check XML format and URL accessibility
- **Download fails**: Verify installer URL and file permissions
- **Installation fails**: Test installer with command-line arguments
- **Service conflicts**: Ensure service stops before update

### Logging
- Check application logs for update-related errors
- AutoUpdater events are logged via `_logService`
- Windows Event Viewer for installer-related issues

## GitHub Integration Option

For GitHub-based releases:
1. Use GitHub Releases API
2. Host update.xml in your repository
3. Use GitHub Actions for automated deployment
4. Benefit from GitHub's CDN and reliability

## Next Steps

1. Choose your hosting solution (web server, GitHub, etc.)
2. Create installer package
3. Set up automated deployment pipeline
4. Test update process thoroughly
5. Deploy to production

Remember to update the URL in `MainForm.cs` InitializeAutoUpdater method before deployment!