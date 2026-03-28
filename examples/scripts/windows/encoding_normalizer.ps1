param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"

function Copy-And-Normalize {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    $textExtensions = @(".nc", ".tap", ".gcode", ".txt", ".cyc", ".xml", ".csv", ".ini")

    Get-ChildItem -Path $SourceRoot -Recurse -File | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($SourceRoot, $_.FullName)
        $destination = Join-Path $DestinationRoot $relative
        $destinationDir = Split-Path -Parent $destination
        if ($destinationDir) {
            New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
        }

        if ($textExtensions -contains $_.Extension.ToLowerInvariant()) {
            $content = [System.IO.File]::ReadAllText($_.FullName)
            $normalized = $content -replace "`r`n", "`n" -replace "`r", "`n"
            $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($destination, $normalized, $utf8NoBom)
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
    Copy-And-Normalize -SourceRoot $SourcePath -DestinationRoot $OutputPath
}
else {
    $destination = Join-Path $OutputPath ([System.IO.Path]::GetFileName($SourcePath))
    $destinationDir = Split-Path -Parent $destination
    if ($destinationDir) {
        New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    }

    $extension = [System.IO.Path]::GetExtension($SourcePath).ToLowerInvariant()
    if (@(".nc", ".tap", ".gcode", ".txt", ".cyc", ".xml", ".csv", ".ini") -contains $extension) {
        $content = [System.IO.File]::ReadAllText($SourcePath)
        $normalized = $content -replace "`r`n", "`n" -replace "`r", "`n"
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($destination, $normalized, $utf8NoBom)
    }
    else {
        Copy-Item -Path $SourcePath -Destination $destination -Force
    }
}

Write-Output "OUTPUT_PATH=$OutputPath"
exit 0
