param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path,
    [string]$Version = "0.1.14",
    [string]$BuildDir = "",
    [string]$DistDir = "",
    [string]$PackId = "3DTek.CNCSync",
    [string]$MainExe = "CNCSync.exe",
    [string]$PackTitle = "CNC Sync",
    [string]$PackAuthors = "3DTek",
    [string]$IconPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BuildDir)) {
    $BuildDir = Join-Path $Root "src\\CNCSync.App\\bin\\Release\\net10.0\\win-x64\\publish"
}

if ([string]::IsNullOrWhiteSpace($DistDir)) {
    $DistDir = Join-Path $Root "dist\\windows\\velopack"
}

if ([string]::IsNullOrWhiteSpace($IconPath)) {
    $IconPath = Join-Path $Root "src\\CNCSync.App\\Assets\\cnc-sync.ico"
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$args = @(
    "pack",
    "--packId", $PackId,
    "--packVersion", $Version,
    "--packDir", $BuildDir,
    "--mainExe", $MainExe,
    "--packTitle", $PackTitle,
    "--packAuthors", $PackAuthors,
    "--outputDir", $DistDir,
    "--runtime", "win-x64",
    "--channel", "win"
)

if (Test-Path $IconPath) {
    $args += @("--icon", $IconPath)
}

& vpk @args

Write-Output "Packaged Velopack Windows release at:"
Write-Output $DistDir
