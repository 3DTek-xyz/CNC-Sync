# GitHub Releases Setup for CBWSS-Sync AutoUpdater

## Repository Configuration: 3DTek-xyz/CNC-FTPSync

### 1. Repository Setup
Your AutoUpdater is now configured to check: `https://raw.githubusercontent.com/3DTek-xyz/CNC-FTPSync/main/update.xml`

### 2. File Structure in GitHub Repository
```
/
├── update.xml                    # Update manifest file (in main branch)
├── src/                         # Your source code
├── releases/                    # Optional: Release documentation
└── .github/
    └── workflows/
        └── release.yml          # Optional: Automated releases
```

### 3. Creating Your First Release

#### Step 1: Prepare Release Files
1. Build your application in Release mode:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained
   ```

2. Create installer package (MSI or Setup.exe) 
3. Name it: `CBWSS-Sync-Setup-v1.1.0.exe`

#### Step 2: Update version.xml
Update the file in your main branch:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>1.1.0.0</version>
    <url>https://github.com/3DTek-xyz/CNC-FTPSync/releases/download/v1.1.0/CBWSS-Sync-Setup-v1.1.0.exe</url>
    <changelog>https://github.com/3DTek-xyz/CNC-FTPSync/releases/tag/v1.1.0</changelog>
    <mandatory>false</mandatory>
    <args>/SILENT</args>
    <checksum algorithm="SHA512">ACTUAL_CHECKSUM_HERE</checksum>
</item>
```

#### Step 3: Generate Checksum
```bash
# Windows
certutil -hashfile CBWSS-Sync-Setup-v1.1.0.exe SHA512

# macOS/Linux  
shasum -a 512 CBWSS-Sync-Setup-v1.1.0.exe
```

#### Step 4: Create GitHub Release
1. Go to: https://github.com/3DTek-xyz/CNC-FTPSync/releases
2. Click "Create a new release"
3. Tag version: `v1.1.0`
4. Release title: `CBWSS-Sync v1.1.0`
5. Upload your installer file as an asset
6. Write release notes
7. **Keep "Set as the latest release" checked**
8. Click "Publish release"

### 4. Repository Privacy Settings
- ✅ Repository can be **private** (source code stays private)
- ✅ Releases will be **public** (users can download without repo access)
- ✅ update.xml file in main branch needs to be accessible

### 5. Making update.xml Accessible from Private Repo
Since your repo will be private, you have two options for update.xml:

#### Option A: Use GitHub Pages (Recommended)
1. Enable GitHub Pages in repository settings
2. Set source to "Deploy from a branch" → main
3. The update.xml will be accessible at: 
   `https://3dtek-xyz.github.io/CNC-FTPSync/update.xml`
4. Update AutoUpdater URL in code to use this URL

#### Option B: Use Raw GitHub URL (Current Setup)
- Requires update.xml to be in a public branch or public repo
- Currently configured for: `https://raw.githubusercontent.com/3DTek-xyz/CNC-FTPSync/main/update.xml`

### 6. Update Workflow Process

#### When Releasing New Version:
1. **Build & Package** your application
2. **Generate Checksum** of installer
3. **Update update.xml** with new version info
4. **Commit update.xml** to main branch
5. **Create GitHub Release** with installer as asset
6. **Test update process**

#### Version Numbering:
- Use semantic versioning: `Major.Minor.Patch.Build`
- Example progression: `1.0.0.0` → `1.0.1.0` → `1.1.0.0` → `2.0.0.0`
- Tag releases as: `v1.1.0`, `v1.2.0`, etc.

### 7. Automated Release Workflow (Optional)
Create `.github/workflows/release.yml`:
```yaml
name: Create Release
on:
  push:
    tags:
      - 'v*'
jobs:
  release:
    runs-on: windows-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '9.0.x'
    - name: Build Release
      run: dotnet publish -c Release -r win-x64 --self-contained
    - name: Create Release
      uses: softprops/action-gh-release@v1
      with:
        files: |
          path/to/installer.exe
```

### 8. Testing Your Setup

#### Test Update Detection:
1. Set a higher version number in update.xml (e.g., `1.2.0.0`)
2. Commit to main branch
3. Run your application
4. Check if update is detected

#### Test Download Process:
1. Create actual release with installer
2. Verify download URL works
3. Test installer with silent installation

### 9. Security Considerations
- ✅ Always use HTTPS URLs
- ✅ Include SHA512 checksums
- ✅ Test installer integrity
- ✅ Keep source code private while allowing public releases

### 10. Troubleshooting
- **update.xml not accessible**: Check if file is in main branch of public repo, or enable GitHub Pages
- **Release not found**: Verify release is published and marked as "latest"
- **Download fails**: Check if installer file was uploaded as release asset
- **Wrong checksum**: Regenerate checksum after any file changes

Your AutoUpdater is now configured for your GitHub repository! 
Next steps: Create your first release to test the process.