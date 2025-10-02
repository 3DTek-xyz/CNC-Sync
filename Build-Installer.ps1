# Build-Installer.ps1
# PowerShell script to build the CBWSS-Sync MSI installer on Windows

Write-Host "Building CBWSS-Sync Installer..." -ForegroundColor Green

# Check if WiX is installed
$wixInstalled = Get-Command "wix" -ErrorAction SilentlyContinue
if (-not $wixInstalled) {
    Write-Host "Installing WiX Toolset..." -ForegroundColor Yellow
    dotnet tool install --global wix
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to install WiX Toolset. Please install manually."
        exit 1
    }
}

# Build Release configuration first
Write-Host "Building Release configuration..." -ForegroundColor Yellow
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build Release configuration."
    exit 1
}

# Build the installer
Write-Host "Building MSI installer..." -ForegroundColor Yellow
dotnet build src\CBWSSSync.Installer\CBWSSSync.Installer.wixproj --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build installer."
    exit 1
}

# Check if MSI was created
$msiPath = "bin\Release\CBWSSSync.Installer.msi"
if (Test-Path $msiPath) {
    Write-Host "✅ Installer created successfully: $msiPath" -ForegroundColor Green
    $msi = Get-Item $msiPath
    Write-Host "   Size: $([math]::Round($msi.Length / 1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "   Created: $($msi.CreationTime)" -ForegroundColor Gray
} else {
    Write-Error "❌ Installer file not found at: $msiPath"
    exit 1
}

Write-Host "`n🎉 Build completed successfully!" -ForegroundColor Green
Write-Host "Installer location: $(Resolve-Path $msiPath)" -ForegroundColor White

# Optional: Test installation
$testInstall = Read-Host "`nWould you like to test the installer? (y/N)"
if ($testInstall -eq 'y' -or $testInstall -eq 'Y') {
    Write-Host "Testing installer (this will install the application)..." -ForegroundColor Yellow
    Start-Process -FilePath $msiPath -Wait
}