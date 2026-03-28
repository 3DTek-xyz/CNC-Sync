# GitHub Actions Automated Build & Release

## Overview

This repository uses GitHub Actions to automatically build MSI installers on Windows runners, eliminating the need for local Windows development environments.

## Workflows

### 1. Test Build (`test-build.yml`)

**Triggers:**
- Every push to `main` branch
- Pull requests to `main` branch  
- Manual trigger via Actions tab

**What it does:**
- Builds .NET projects on Windows runner
- Creates MSI installer using WiX
- Uploads MSI as artifact for download
- Tests that build process works

### 2. Release Build (`build-release.yml`)

**Triggers:**
- Version tags (e.g., `v1.0.0`, `v1.2.3`)
- Manual trigger via Actions tab

**What it does:**
- Builds complete MSI installer
- Updates `update.xml` with new version info
- Creates GitHub release with MSI attachment
- Generates professional release notes
- Commits updated `update.xml` back to repository

## Usage

### Testing Your Changes

1. **Push code to main branch**
   ```bash
   git add .
   git commit -m "Your changes"
   git push origin main
   ```

2. **Check Actions tab**
   - Go to your repo → Actions tab
   - Watch "Test Build" workflow run
   - Download MSI from artifacts when complete

### Creating a Release

1. **Tag your version**
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. **Automatic process**
   - GitHub Actions builds MSI installer
   - Creates release with professional notes
   - Updates AutoUpdater configuration
   - Users get automatic updates!

## Version Management

### Semantic Versioning
Use tags like:
- `v1.0.0` - Major release
- `v1.0.1` - Bug fix
- `v1.1.0` - New features
- `v2.0.0` - Breaking changes

### Automatic Updates
The workflow:
1. Extracts version from your git tag
2. Updates `Package.wxs` with new version
3. Builds MSI with correct version info
4. Updates `update.xml` with download URLs
5. Commits changes back to repo
6. Creates GitHub release

## File Outputs

### MSI Installer
- **Location**: GitHub Release assets
- **Name**: `CBWSS-Sync-Setup-v1.0.0.msi`
- **Features**: Full Windows integration, service installation, AutoUpdater

### Release Assets
Each release includes:
- MSI installer file
- SHA512 checksum
- Professional release notes
- Installation instructions
- Changelog links

## Benefits

### ✅ No Local Setup Required
- No need for Windows development machine
- No WiX installation on your Mac
- Everything builds in the cloud

### ✅ Consistent Builds
- Same Windows environment every time
- Proper .NET 9.0 + WiX configuration
- No "works on my machine" issues

### ✅ Automated Releases
- Tag → Build → Release → Update users
- Professional release notes generated
- AutoUpdater.NET automatically configured

### ✅ Professional CI/CD
- Proper versioning and changelogs
- Artifact retention and downloads
- Build status badges available

## Customization

### Release Notes Template
Edit the `Create Release Notes` step in `build-release.yml` to customize:
- Features and improvements descriptions
- Installation instructions
- Requirements and compatibility info

### Build Configuration
Modify workflows to:
- Change .NET version
- Add additional build steps
- Include extra files in installer
- Modify WiX configuration

### Notification Options
Add steps for:
- Slack/Teams notifications
- Email alerts on build failures
- Discord webhooks for releases

## Monitoring

### Build Status
- Check Actions tab for build results
- Green ✅ = successful build + release
- Red ❌ = build failure (check logs)

### Release Quality
- Download and test MSI on Windows
- Verify AutoUpdater functionality
- Check service installation/operation

### Usage Analytics
- GitHub provides download statistics
- Monitor release adoption
- Track update success rates

## Security

### Secrets Management
The workflow uses:
- `GITHUB_TOKEN` (automatically provided)
- No additional secrets required
- Secure Windows runner environment

### Code Signing (Future)
To add code signing:
1. Get code signing certificate
2. Add certificate as GitHub secret
3. Modify workflow to sign MSI
4. Eliminates Windows security warnings

This automated approach gives you professional-grade releases with minimal manual work! 🚀