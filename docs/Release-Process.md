# Release Process Guide

This guide explains how to create GitHub releases for the AGI PDM Migrator.

## Prerequisites

- .NET 8.0 SDK installed
- Access to create releases on the GitHub repository

## Building for Release

1. **Update version number** (if using versioning in the project file)

2. **Build the release version**:
   ```powershell
   cd AGI-PDM
   dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
   ```

3. **Create release package**:
   ```powershell
   # Create a release folder
   New-Item -ItemType Directory -Path "../release" -Force
   
   # Copy necessary files
   Copy-Item "bin/Release/net8.0/win-x64/publish/AGI-PDM.exe" "../release/"
   Copy-Item "config.json" "../release/"
   Copy-Item "../README.md" "../release/"
   Copy-Item "../scripts/Install-AGIPDMMigrator.ps1" "../release/"
   
   # Create zip archive
   Compress-Archive -Path "../release/*" -DestinationPath "../AGI-PDM-Migrator.zip" -Force
   ```

## Creating a GitHub Release

### Using GitHub Web Interface

1. Go to https://github.com/jwraynor/AGI.PDM-Migrator/releases
2. Click "Create a new release"
3. Choose a tag (create new): `v1.0.0` (or appropriate version)
4. Set release title: "AGI PDM Migrator v1.0.0"
5. Write release notes:
   ```markdown
   ## AGI PDM Migrator v1.0.0
   
   Initial release of the AGI PDM Server Migration Tool.
   
   ### Features
   - Automated migration of SolidWorks PDM vault views
   - Pre-flight checks to ensure system readiness
   - Registry backup before modifications
   - Automatic cleanup of local cache
   - Autonomous mode for RMM deployment
   - Detailed logging for troubleshooting
   
   ### Installation
   
   Download `AGI-PDM-Migrator.zip` and extract to a folder.
   
   ### Quick Start
   
   For RMM deployment, use the bootstrap script:
   ```powershell
   .\Install-AGIPDMMigrator.ps1
   ```
   
   ### Requirements
   - Windows 10/11 or Windows Server 2016+
   - .NET 8.0 Runtime
   - SolidWorks PDM Client installed
   - Administrator privileges
   ```

6. Upload the `AGI-PDM-Migrator.zip` file
7. Check "Set as the latest release"
8. Click "Publish release"

### Using GitHub CLI

If you have GitHub CLI installed:

```bash
# Create release and upload assets
gh release create v1.0.0 \
  --title "AGI PDM Migrator v1.0.0" \
  --notes-file release-notes.md \
  AGI-PDM-Migrator.zip
```

## Build Script

Create a `Build-Release.ps1` script for automated builds:

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

Write-Host "Building AGI PDM Migrator v$Version..." -ForegroundColor Green

# Clean previous builds
Remove-Item -Path "AGI-PDM/bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "AGI-PDM/obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "release" -Recurse -Force -ErrorAction SilentlyContinue

# Build
Set-Location "AGI-PDM"
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
Set-Location ".."

# Create release package
New-Item -ItemType Directory -Path "release" -Force | Out-Null
Copy-Item "AGI-PDM/bin/Release/net8.0/win-x64/publish/AGI-PDM.exe" "release/"
Copy-Item "AGI-PDM/config.json" "release/"
Copy-Item "README.md" "release/"
Copy-Item "scripts/Install-AGIPDMMigrator.ps1" "release/"

# Create zip
$zipName = "AGI-PDM-Migrator-v$Version.zip"
Compress-Archive -Path "release/*" -DestinationPath $zipName -Force

Write-Host "Release package created: $zipName" -ForegroundColor Green
Write-Host "Ready to upload to GitHub releases!" -ForegroundColor Yellow
```

## Version Numbering

Use semantic versioning (SemVer):
- MAJOR.MINOR.PATCH (e.g., 1.0.0)
- MAJOR: Breaking changes
- MINOR: New features (backwards compatible)
- PATCH: Bug fixes

## Release Checklist

- [ ] Update version in project file (if applicable)
- [ ] Update README.md with any new features
- [ ] Test the release build
- [ ] Create release notes
- [ ] Build release package
- [ ] Create GitHub release
- [ ] Upload release assets
- [ ] Test bootstrap script with new release

## Automating with GitHub Actions

You can automate releases using GitHub Actions. Create `.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x
    
    - name: Build
      run: |
        cd AGI-PDM
        dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
    
    - name: Package
      run: |
        mkdir release
        copy AGI-PDM\bin\Release\net8.0\win-x64\publish\AGI-PDM.exe release\
        copy AGI-PDM\config.json release\
        copy README.md release\
        copy scripts\Install-AGIPDMMigrator.ps1 release\
        Compress-Archive -Path release\* -DestinationPath AGI-PDM-Migrator.zip
    
    - name: Create Release
      uses: softprops/action-gh-release@v1
      with:
        files: AGI-PDM-Migrator.zip
        draft: false
        prerelease: false
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

This will automatically create a release whenever you push a version tag.