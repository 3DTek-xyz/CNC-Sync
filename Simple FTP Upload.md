

When Simple FTP method is being used 

1. Watch folder as per usual.
2. When triggered with new file or folder creation collect this first triggers timestamp and the triggered folder path.
3. Keep watching the root folder for updates until the stability expires.
4. Any further triggers will not update the timestamp.
5. Further triggers will update the folder path if that triggered folder is closer to root than the current captured folder path.
6. Further triggers will reset the stability timout.

Once Stability is reached:
7. All files are assessed for creattion/update time > timestamp -  recusivly in the captured folder path 
8. If the file is > timetsamp then it will need to be uploaded to ftp server.
9. If the file was in root then obviosly no root folder need be created
10. If the file was deeper than the root then its possible that the folder structure to at least this depth will need to be created.

## SIMPLIFIED Implementation Plan

### Approach
Use simpler root folder stability approach - scan all files recursively in root when stable, upload changed files directly to FTP server.

### Step 1: Fix Broken Code
- Revert ProcessIndividualFilesAsync to restore compilation
- Restore original file copying logic temporarily

### Step 2: Verify Current FolderWatcher Behavior  
- Confirm current behavior: first trigger sets timestamp, subsequent triggers reset stability timeout
- Current behavior already preserves original timestamp (doesn't update on further triggers) ✅
- Current behavior already waits for stability ✅

### Step 3: Modify ProcessIndividualFilesAsync for Direct FTP Upload
```csharp
// Keep existing logic:
// - Get timestamp from FolderWatcher ✅  
// - Get files created since timestamp ✅
// - Recursive scanning ✅

// Change only the file processing loop:
foreach (var filePath in recentFiles)
{
    // Calculate relative path from watch folder
    var relativePath = Path.GetRelativePath(_config.WatchFolder, filePath);
    
    // Create FTP directory if needed (skip if file in root)
    var ftpDir = Path.GetDirectoryName(relativePath);
    if (!string.IsNullOrEmpty(ftpDir))
    {
        await _ftpService.CreateDirectoryAsync(ftpDir);
    }
    
    // Upload file directly to FTP server
    await _ftpService.UploadFileAsync(filePath, relativePath);
}
```

### Step 4: Remove Local Staging
- ProcessSimpleFtpUploadAsync should NOT set result.OutputPath
- No local FTP directory copying needed
- Files go directly from source to FTP server

### Step 5: Error Handling
- Log successful uploads: source -> FTP destination
- Continue processing if individual uploads fail
- Clear timestamp only after processing completes

### Current vs Simple FTP
- **Current**: Trigger → Stability → Copy to local FTP dir → Bulk upload directory
- **Simple FTP**: Trigger → Stability → Scan files → Upload individual files directly


