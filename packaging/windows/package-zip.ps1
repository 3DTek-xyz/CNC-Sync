param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path,
    [string]$Version = "0.1.8",
    [string]$BuildDir = "",
    [string]$DistDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BuildDir)) {
    $BuildDir = Join-Path $Root "src\\CNCSync.App\\bin\\Release\\net10.0\\win-x64\\publish"
}

if ([string]::IsNullOrWhiteSpace($DistDir)) {
    $DistDir = Join-Path $Root "dist\\windows"
}

$ZipPath = Join-Path $DistDir "cnc-sync-windows-x64-v$Version.zip"

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path (Join-Path $BuildDir "*") -DestinationPath $ZipPath

Write-Output "Packaged zip at:"
Write-Output $ZipPath
