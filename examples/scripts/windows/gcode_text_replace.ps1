param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$SearchText = "G90",
    [string]$ReplacementText = "G90",
    [string]$FileGlob = "*.nc"
)

$ErrorActionPreference = "Stop"

function Should-Transform {
    param([string]$FileName)
    return [System.Management.Automation.WildcardPattern]::Get($FileGlob, "IgnoreCase").IsMatch($FileName)
}

function Process-Tree {
    param([string]$SourceRoot, [string]$DestinationRoot)
    Get-ChildItem -Path $SourceRoot -Recurse -File | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($SourceRoot, $_.FullName)
        $destination = Join-Path $DestinationRoot $relative
        $destinationDir = Split-Path -Parent $destination
        if ($destinationDir) {
            New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
        }

        if (Should-Transform $_.Name) {
            $content = [System.IO.File]::ReadAllText($_.FullName)
            $updated = $content.Replace($SearchText, $ReplacementText)
            $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($destination, $updated, $utf8NoBom)
        }
        else {
            Copy-Item -Path $_.FullName -Destination $destination -Force
        }
    }
}

if (-not (Test-Path $SourcePath)) {
    throw "Source path does not exist: $SourcePath"
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

if (Test-Path $SourcePath -PathType Container) {
    Process-Tree -SourceRoot $SourcePath -DestinationRoot $OutputPath
}
else {
    $destination = Join-Path $OutputPath ([System.IO.Path]::GetFileName($SourcePath))
    if (Should-Transform ([System.IO.Path]::GetFileName($SourcePath))) {
        $content = [System.IO.File]::ReadAllText($SourcePath)
        $updated = $content.Replace($SearchText, $ReplacementText)
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($destination, $updated, $utf8NoBom)
    }
    else {
        Copy-Item -Path $SourcePath -Destination $destination -Force
    }
}

Write-Output "OUTPUT_PATH=$OutputPath"
exit 0
