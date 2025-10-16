# SimpleFolderCopy.ps1
# Demo PowerShell script for CNC-FTP-SYNC external processing
# 
# This script demonstrates how to create custom processing logic that replaces
# the built-in G-code file processing when "Use External Script" is enabled
# in the CNC-FTP-SYNC configuration.
#
# PARAMETERS:
# The CNC-FTP-SYNC system calls this script with 4 parameters:
# 1. ProjectPath     - Full path to the source folder that was detected (e.g., "C:\Watch\NewProject_Rev1")  
# 2. FtpDestination  - Target FTP upload path (e.g., "Upload/NewProject" or just "NewProject")
# 3. ProjectName     - Extracted project name (e.g., "NewProject") 
# 4. Revision        - Extracted revision or "unknown" (e.g., "Rev1" or "unknown")
#
# USAGE:
# To use this script:
# 1. Enable "Use External Script for processing" in CNC-FTP-SYNC configuration
# 2. Browse and select this script (or your customized version)
# 3. The script will be called instead of built-in G-code processing
#
# CUSTOMIZATION:
# Copy this file and modify it to implement your specific processing needs:
# - Custom file filtering (not just .gcode files)
# - File transformation or validation
# - Integration with other systems
# - Custom logging or reporting
# - Advanced folder structure handling

param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectPath,      # Source folder path
    
    [Parameter(Mandatory=$true)] 
    [string]$FtpDestination,   # Target FTP upload path
    
    [Parameter(Mandatory=$true)]
    [string]$ProjectName,      # Project name
    
    [Parameter(Mandatory=$true)]
    [string]$Revision          # Revision or "unknown"
)

# Function to write log messages with timestamps
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$timestamp] [$Level] $Message"
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

# Main processing logic starts here
Write-Log "=== CNC-FTP-SYNC External Script Processing Started ==="
Write-Log "Script: SimpleFolderCopy.ps1"
Write-Log "ProjectPath: $ProjectPath"
Write-Log "FtpDestination: $FtpDestination" 
Write-Log "ProjectName: $ProjectName"
Write-Log "Revision: $Revision"

# Validate that source folder exists
if (-not (Test-Path $ProjectPath -PathType Container)) {
    Write-Log "ERROR: Source folder does not exist: $ProjectPath" "ERROR"
    exit 1
}

# Get the configured FTP upload directory from CNC-FTP-SYNC
# In this demo, we assume it's a local folder for simplicity
# In real scenarios, this might be the local FTP staging area
$baseUploadPath = Split-Path $FtpDestination -Parent
if ([string]::IsNullOrEmpty($baseUploadPath)) {
    # If no parent path, assume current working directory or default upload location
    $baseUploadPath = Join-Path $env:TEMP "CNC-FTP-Upload"
}

# Create the full destination path  
$fullDestinationPath = Join-Path $baseUploadPath $ProjectName

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
$sourceFiles = Get-ChildItem -Path $ProjectPath -File -Recurse

Write-Log "Found $($sourceFiles.Count) files to process"

# Copy all files, preserving folder structure
$successCount = 0
$failCount = 0

foreach ($file in $sourceFiles) {
    # Calculate relative path from source root
    $relativePath = $file.FullName.Substring($ProjectPath.Length).TrimStart('\', '/')
    
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
Write-Log "Destination: $fullDestinationPath"
Write-Log "==============================================="

# Exit with appropriate code
if ($failCount -eq 0) {
    Write-Log "Script completed successfully" "SUCCESS"
    exit 0
} else {
    Write-Log "Script completed with errors" "ERROR" 
    exit 1
}