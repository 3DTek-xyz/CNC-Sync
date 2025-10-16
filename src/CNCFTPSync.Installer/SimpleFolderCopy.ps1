# SimpleFolderCopy.ps1
# Demo PowerShell script for CNC-FTP-SYNC external processing
# 
# This script demonstrates how to create custom processing logic that replaces
# the built-in G-code file processing when "Use External Script" is enabled
# in the CNC-FTP-SYNC configuration.
#
# PARAMETERS:
# The CNC-FTP-SYNC system calls this script with 3 parameters:
# 1. SourcePath      - Full path to the source folder that was detected by file watcher (e.g., "C:\Watch\NewProject_Rev1")  
# 2. FtpUploadPath   - Base FTP upload directory path (e.g., "C:\FTPUpload" or "\\server\upload")
# 3. LogFilePath     - Path to the main CNC-FTP-SYNC log file for integrated logging
#
# RETURN VALUES:
# The script must return:
# 1. Exit Code: 0 for success, non-zero for failure
# 2. Output Path: The script outputs the full path to the prepared files (via stdout)
#
# USAGE:
# To use this script:
# 1. Enable "Use External Script for processing" in CNC-FTP-SYNC configuration
# 2. Browse and select this script (or your customized version)
# 3. The script will be called instead of built-in G-code processing
#
# CUSTOMIZATION:
# Copy this file and modify it to implement your specific processing needs:
# - Custom file filtering and processing
# - File transformation or validation
# - Integration with other systems
# - Custom naming and folder structure
# - Advanced error handling and logging

param(
    [Parameter(Mandatory=$true)]
    [string]$SourcePath,       # Source folder detected by watcher
    
    [Parameter(Mandatory=$true)] 
    [string]$FtpUploadPath,    # Base FTP upload directory
    
    [Parameter(Mandatory=$true)]
    [string]$LogFilePath       # Main application log file path
)

#############################################################################
# HELPER FUNCTIONS
#############################################################################

# Function to write log messages with timestamps
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] [External Script] $Message"
    
    # Only write to log file, NOT to stdout - stdout is reserved for return path
    try {
        if (-not [string]::IsNullOrEmpty($LogFilePath) -and (Test-Path (Split-Path $LogFilePath -Parent))) {
            Add-Content -Path $LogFilePath -Value $logEntry -Encoding UTF8
        }
    }
    catch {
        # If log file writing fails, write to stderr instead of stdout to avoid polluting return path
        Write-Error "[$timestamp] [WARNING] [External Script] Failed to write to log file: $($_.Exception.Message)"
    }
}

# Function to safely copy files with error handling
function Copy-FileSafely {
    param(
        [string]$Source,
        [string]$Destination
    )
    
    try {
        # Ensure destination directory exists
        $destDir = Split-Path $Destination -Parent
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        
        Copy-Item -Path $Source -Destination $Destination -Force
        Write-Log "Copied: $(Split-Path $Source -Leaf) -> $(Split-Path $Destination -Leaf)"
        return $true
    }
    catch {
        Write-Log "Failed to copy $Source to $Destination : $($_.Exception.Message)" "ERROR"
        return $false
    }
}

#############################################################################
# Main processing logic starts here
Write-Log "=== CNC-FTP-SYNC External Script Processing Started ==="
Write-Log "Script: SimpleFolderCopy.ps1"
Write-Log "SourcePath: $SourcePath"
Write-Log "FtpUploadPath: $FtpUploadPath"
Write-Log "LogFilePath: $LogFilePath"

# Validate that source folder exists
if (-not (Test-Path $SourcePath -PathType Container)) {
    Write-Log "ERROR: Source folder does not exist: $SourcePath" "ERROR"
    exit 1
}

# Validate that FTP upload base path exists
if (-not (Test-Path $FtpUploadPath -PathType Container)) {
    Write-Log "ERROR: FTP upload path does not exist: $FtpUploadPath" "ERROR"
    exit 1
}

# Extract folder name from source path for destination
$sourceFolderName = Split-Path $SourcePath -Leaf

# Create the full destination path within FTP upload directory
$fullDestinationPath = Join-Path $FtpUploadPath $sourceFolderName

Write-Log "Source folder name: $sourceFolderName"
Write-Log "Destination folder: $fullDestinationPath"

# Create destination directory if it doesn't exist
try {
    if (-not (Test-Path $fullDestinationPath)) {
        New-Item -ItemType Directory -Path $fullDestinationPath -Force | Out-Null
        Write-Log "Created destination directory: $fullDestinationPath"
    }
}
catch {
    Write-Log "Failed to create destination directory: $($_.Exception.Message)" "ERROR"
    exit 1
}

# Get all files from source folder recursively
$sourceFiles = Get-ChildItem -Path $SourcePath -File -Recurse

Write-Log "Found $($sourceFiles.Count) files to process"

# Copy all files, preserving folder structure
$successCount = 0
$failCount = 0

foreach ($file in $sourceFiles) {
    # Calculate relative path from source root
    $relativePath = $file.FullName.Substring($SourcePath.Length).TrimStart('\', '/')
    
    # Build destination path maintaining folder structure
    $destFile = Join-Path $fullDestinationPath $relativePath
    
    # Copy the file
    if (Copy-FileSafely -Source $file.FullName -Destination $destFile) {
        $successCount++
    }
    else {
        $failCount++
    }
}

# Report results
Write-Log "=== Processing Complete ==="
Write-Log "Successfully copied: $successCount files"
if ($failCount -gt 0) {
    Write-Log "Failed to copy: $failCount files" "ERROR"
}
Write-Log "Prepared files location: $fullDestinationPath"
Write-Log "==============================================="

# Output the path to prepared files (for service to capture)
if ($failCount -eq 0) {
    Write-Log "Script completed successfully" "SUCCESS"
    # Output the destination path to stdout with Path= prefix for easy parsing
    Write-Output "Path=$fullDestinationPath"
    exit 0
} else {
    Write-Log "Script completed with errors" "ERROR" 
    exit 1
}