param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$UpdateCycY,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)

$ErrorActionPreference = "Stop"

if ($RemainingArguments -contains "--update-cyc-y") {
    $UpdateCycY = $true
}

if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
    Write-Error "legacy_revision expects a folder source path."
    exit 1
}

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$ncPath = Join-Path $OutputPath "NC"
$labelPath = Join-Path $OutputPath "AutoStickLabel"
New-Item -ItemType Directory -Path $ncPath -Force | Out-Null
New-Item -ItemType Directory -Path $labelPath -Force | Out-Null

$allFiles = Get-ChildItem -LiteralPath $SourcePath -File -Recurse
$cycFiles = $allFiles | Where-Object {
    $_.Extension -ieq ".cyc" -and -not $_.Name.StartsWith("ORIGINAL_", [System.StringComparison]::OrdinalIgnoreCase)
}

$latestRevision = $cycFiles |
    ForEach-Object {
        if ($_.Name -match 'R(\d{2})\.cyc$') {
            [int]$matches[1]
        }
    } |
    Sort-Object -Descending |
    Select-Object -First 1

if ($null -eq $latestRevision) {
    Write-Error "No CYC files with revision markers were found."
    exit 1
}

$revisionTag = "R{0:D2}" -f $latestRevision

foreach ($file in $allFiles) {
    $extension = $file.Extension.ToLowerInvariant()

    if ($extension -eq ".nc" -and $file.Name -match ([regex]::Escape($revisionTag) + '\.nc$')) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ncPath $file.Name) -Force
        continue
    }

    if ($extension -eq ".cyc" -and
        $file.Name -match ([regex]::Escape($revisionTag) + '\.cyc$') -and
        -not $file.Name.StartsWith("ORIGINAL_", [System.StringComparison]::OrdinalIgnoreCase)) {
        $destination = Join-Path $labelPath $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force

        if ($UpdateCycY) {
            $content = Get-Content -LiteralPath $destination -Raw -Encoding UTF8
            $updated = [regex]::Replace($content, '(<Field Name="Y" Value=")-([\d\.]+(".*?>))', '$1$2$3')
            [System.IO.File]::WriteAllText($destination, $updated, [System.Text.UTF8Encoding]::new($false))
        }

        continue
    }
}

$rootLevelFiles = Get-ChildItem -LiteralPath $SourcePath -File
foreach ($file in $rootLevelFiles) {
    $extension = $file.Extension.ToLowerInvariant()
    if ($extension -in @(".xml", ".jpg", ".jpeg")) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $labelPath $file.Name) -Force
    }
}

Write-Output "OUTPUT_PATH=$OutputPath"
exit 0
