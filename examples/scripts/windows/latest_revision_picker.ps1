param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SourcePath -PathType Container)) {
    throw "latest_revision_picker expects a folder source path."
}

$revisionPattern = [regex]'R(\d{2})'
$files = Get-ChildItem -Path $SourcePath -Recurse -File
$revisioned = $files | Where-Object { $revisionPattern.IsMatch($_.Name) }

$latestRevision = $null
if ($revisioned.Count -gt 0) {
    $latestRevision = ($revisioned | ForEach-Object {
        [int]$revisionPattern.Match($_.Name).Groups[1].Value
    } | Sort-Object | Select-Object -Last 1)
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

foreach ($file in $files) {
    $copy = $true
    if ($latestRevision -ne $null -and $revisionPattern.IsMatch($file.Name)) {
        $copy = [int]$revisionPattern.Match($file.Name).Groups[1].Value -eq $latestRevision
    }

    if ($copy) {
        $relative = [System.IO.Path]::GetRelativePath($SourcePath, $file.FullName)
        $destination = Join-Path $OutputPath $relative
        $destinationDir = Split-Path -Parent $destination
        if ($destinationDir) {
            New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
        }
        Copy-Item -Path $file.FullName -Destination $destination -Force
    }
}

Write-Output "OUTPUT_PATH=$OutputPath"
exit 0
