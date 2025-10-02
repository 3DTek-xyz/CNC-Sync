<#
To DO:
//0. 
1. Check to see if it was a 'folder' and type "created" file system event that trigger this script, set folder as active project to process
2. Check for and delete / recreate any XML, NC, or AutoLabel Folders
3. Find most recently updated revision in this Project
4. Move NC Files to \NC folder
5. Convert CYC Files to UTF-8 then Move cyc Files to \Xml folder
6. Move JPG label Files to \AutoLabelPath folder
7. Move .xml files  to \AutoLabelPath folder 
8. Process .CYC coordinate updates
9. Create folder for job / rev in ftp uploads and MOVE (not copy) nc & autolabel folders into it  (file watcher will trigger ftp sync)
#>

# Call example: powershell.exe -file C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\CNCSync\ProcessGcodeToFtpFolder-EmilsPC.ps1  -change_path "%change_path%" -change_action "%change_action%"
# Call example: powershell.exe -file C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\CNCSync\TESTSCRIPT.ps1  -change_path "%change_path%" -change_action "%change_action%"
# Script Will fail without these requred parameters

param(
    [Parameter(Mandatory=$True, Position=0, ValueFromPipeline=$false)]
    [System.String]
    $change_action,

    [Parameter(Mandatory=$True, Position=1, ValueFromPipeline=$false)]
    [System.String]
    $change_path
)


Write-Host $change_action
Write-Host $change_path


$logStr = ""

# -Param1 %change_action% -Param2 %change_path% 

# Enter the location of the destination folder to search:
#$destinationFolder = "C:\Users\Reception\Dropbox\MozaikShared\CUSTOMBUILT\GCODE"
#$destinationFolder = "C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\GCODE"
#$destinationFolder = "C:\Mac\Home\Desktop\TESTBEN"
$FtpAutoUploadFolder = "C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\FTPUpload"
#$FtpAutoUploadFolder = "C:\Users\Reception\Dropbox\MozaikShared\CUSTOMBUILT\FTPUpload"
#$FtpAutoUploadFolder = "C:\Mac\Home\Desktop\FTPUpload"

# DO NOT CHANGE ANYTHING BELOW THIS LINE
# ------------------------------------------------------------------------------

try {

clear



#1 Check to see if it was a type "created" or "Update" file system event that trigger this script, set folder as active project to process

if (( $change_action -ne "Create" -and $change_action -ne "Update")){
    "Exit1: $change_action" >> C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\CNCSync\SyncLOG.txt
    exit
    } 
else{
    if (-Not (Test-Path -Path $change_path -PathType Leaf)){
        $destinationFolder = $change_path
    }
    else{
        $destinationFolder = Split-Path -Path $change_path -Parent -Resolve
    }
    "New Code To Process full path: $destinationFolder" >> C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\CNCSync\SyncLOG.txt
}

#$latest =(gci $destinationFolder  | ? { $_.PSIsContainer } | sort LastWriteTime)[-1]
$LatestProjPath = $destinationFolder

#Write-Host "Latest Updated Project Path: " $LatestProjPath


#2 Check for and delete / recreate any XML, NC, or AutoStickLabel Folders
#$XMLPath = Join-Path $LatestProjPath "Xml"
$NCPath = Join-Path $LatestProjPath "NC"
$AutoLabelPath = Join-Path $LatestProjPath "AutoStickLabel"
$paths = $NCPath, $AutoLabelPath #$XMLPath, 

foreach ($path in $paths) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path  -Recurse #-WhatIf -Verbose
        New-Item -ItemType Directory -Path $path | Out-Null
        Write-Host "Deleted and Recreated folder: $path"
    } else {
        New-Item -ItemType Directory -Path $path | Out-Null
        "Path Didnt exist yet, so created it: $path"
    }
}

#3. Find most recently updated revision in this mozaik gcode Project
 Write-Host "Find Most Recent Revision"
     $files = Get-ChildItem -Path $LatestProjPath -Recurse -Filter *.cyc | Where-Object {
        -not ($_.Name -like "ORIGINAL_*")
    }

    $cycfiles = @()
    foreach ($fileItem in $files) {
        $file = $fileItem.Name
        $i = $fileItem.Name.LastIndexOf("R") 
        $cycfiles += $fileItem.Name.Substring($i+1,2)
        }
    $LatestRev = $cycfiles | Sort-Object | select -Last 1
    Write-Host "Latest REV: $LatestRev."



#4. Move NC Files to \NC folder
    Write-Host "Searching for .NC files in: $LatestProjPath"
    $files = Get-ChildItem -Path $LatestProjPath -Recurse -Filter *R$LatestRev.nc
    foreach ($fileItem in $files) {
        $file = $fileItem.FullName
        $savepath = Join-Path $NCPath $fileItem.Name
        Move-Item -Path $file -Destination $savepath -Verbose
        }

#5.  CYC Files move cyc Files to \Xml folder
    
    Write-Host "Searching for .cyc files in: $LatestProjPath"
    

    
    #Move Files
    $files = Get-ChildItem -Path $LatestProjPath -Recurse -Filter *R$LatestRev.cyc 
    foreach ($fileItem in $files) {
        $file = $fileItem.FullName
        $savepath = Join-Path $AutoLabelPath $fileItem.Name
        Move-Item -Path $file -Destination $savepath -Verbose
        }



#6. Move JPG label Files to \AutoLabelPath folder
    Write-Host "Searching for .JPG files in: $LatestProjPath"
    $files = Get-ChildItem -Path $LatestProjPath -Recurse -Filter *.JPG
    foreach ($fileItem in $files) {
        $file = $fileItem.FullName
        $savepath = Join-Path $AutoLabelPath $fileItem.Name
        Move-Item -Path $file -Destination $savepath -Verbose
        }

#7. Move .xml files  to \AutoLabelPath folder 
    Write-Host "Searching for .xml files in: $LatestProjPath"
    $files = Get-ChildItem -Path $LatestProjPath -Recurse -Filter *.xml
    foreach ($fileItem in $files) {
        $file = $fileItem.FullName
        $savepath = Join-Path $AutoLabelPath $fileItem.Name
        Move-Item -Path $file -Destination $savepath -Verbose
        }


#8. Process .CYC coordinate updates


    Write-Host "Searching for .cyc files in: $AutoLabelPath"

    $files = Get-ChildItem -Path $AutoLabelPath -Recurse -Filter *.cyc | Where-Object {
        -not ($_.Name -like "ORIGINAL_*")
    }

    foreach ($fileItem in $files) {
        $file = $fileItem.FullName
        $folder = $fileItem.DirectoryName
        #$originalFileName = "ORIGINAL_" + $fileItem.Name

        <#
        # Create subfolder for backups
        $originalFolder = Join-Path $folder "Original CYC"
        if (-not (Test-Path $originalFolder)) {
            New-Item -ItemType Directory -Path $originalFolder | Out-Null
            Write-Host "Created backup folder: $originalFolder"
        }

        $originalPath = Join-Path $originalFolder $originalFileName

        Write-Host "Processing: $file"

        # Create backup in subfolder
        if (-not (Test-Path $originalPath)) {
            Copy-Item -Path $file -Destination $originalPath
            Write-Host "Backup created: $originalFileName"
        } else {
            Write-Host "Backup already exists: $originalFileName"
        }
        #>

        # Load and process XML
        try {
            [xml]$xml = Get-Content -Path $file -Raw -Encoding Unicode
        } catch {
            throw "Failed to read XML from file: $file`nReason: $($_.Exception.Message)"
        }

        $fieldsToUpdate = $xml.SelectNodes("//Field[@Name='Y']")
        foreach ($field in $fieldsToUpdate) {
            $currentValue = [double]$field.Value
            if ($currentValue -lt 0) {
                $newValue = [math]::Abs($currentValue)
                $field.Value = $newValue.ToString()
                Write-Host "Y value updated: $currentValue to $newValue"
            }
        }

        try {
            $xml.Save($file)
            Write-Host "File updated: $file`n"
        } catch {
            throw "Failed to save updated XML to: $file`nReason: $($_.Exception.Message)"
        }
    }
        Get-ChildItem $AutoLabelPath\*  -recurse -Include *.cyc | ForEach-Object {
        $content = $_ | Get-Content
        Set-Content -PassThru $_.Fullname $content -Encoding UTF8 -Force
        "Convert UTF-8: " + $_.Fullname >> C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\CNCSync\SyncLOG.txt
        }



#9. Create folder for job / rev in ftp uploads and MOVE (not copy) nc & autolabel folders into it  
    #(file watcher will trigger ftp sync)
       # Create subfolder for ftp
       $ProjName = Split-Path $LatestProjPath -Leaf
       "Proj Name:"+ $ProjName >> C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\CNCSync\SyncLOG.txt
       #(Get-Item c:\dir1\dir2\dir3\file.txt).Directory.Name
       #$FtpAutoUploadFolder = [IO.Path]::Combine($FtpAutoUploadFolder, $latest.Name + "-" + $LatestRev)
       $FtpAutoUploadFolder = [IO.Path]::Combine($FtpAutoUploadFolder, $ProjName + "-" + $LatestRev)
        
        if (-not (Test-Path $FtpAutoUploadFolder)) {
            New-Item -ItemType Directory -Path $FtpAutoUploadFolder | Out-Null
            Write-Host "Created ftp proj folder: $FtpAutoUploadFolder"
            Copy-Item -Path $AutoLabelPath -Destination $FtpAutoUploadFolder -recurse -Force
            Copy-Item -Path $NCPath -Destination $FtpAutoUploadFolder -recurse -Force
           
        }
        else{
            Write-Host " HALT  $FtpAutoUploadFolder Already Exists"
        }



    Write-Host "Done. All files processed."
}




catch {
    Write-Host "An error occurred:" -ForegroundColor Red
    Write-Host "Message: $($_.Exception.Message)"
    Write-Host "Script Line: $($_.InvocationInfo.ScriptLineNumber)"
    Write-Host "In File: $($_.InvocationInfo.ScriptName)"
    [System.Windows.MessageBox]:: Show($_.Exception.Message +" in Line: " + $_.InvocationInfo.ScriptLineNumber + "PLEASE SEND THIS MESSAGE TO BEN - 3DTEK") 
    $_.Exception.Message +" in Line: " + $_.InvocationInfo.ScriptLineNumber >> C:\Users\CBWSS\Dropbox\MozaikShared\CUSTOMBUILT\CNCSync\SyncLOG.txt
}
finally {
    Write-Host "`nPress Enter to exit..."
    [void][System.Console]::ReadLine()
}



