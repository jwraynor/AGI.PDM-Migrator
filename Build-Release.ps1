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
Remove-Item -Path "*.zip" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "*.exe" -Force -ErrorAction SilentlyContinue

# Build
Set-Location "AGI-PDM"
Write-Host "Publishing release build..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
Set-Location ".."

# Create release package
Write-Host "Creating release package..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path "release" -Force | Out-Null

# Copy ONLY the executable
Copy-Item "AGI-PDM/bin/Release/net8.0/win-x64/publish/AGI-PDM.exe" "release/"

# Create a sample config
Copy-Item "AGI-PDM/config.json" "release/config.sample.json"

# Create a minimal README for the release
@"
AGI PDM Migrator v$Version
=====================================

Quick Start
-----------
1. Copy config.sample.json to config.json
2. Edit config.json with your server details
3. Run AGI-PDM.exe as Administrator

For automated deployment, download the bootstrap script from:
https://github.com/jwraynor/AGI.PDM-Migrator/blob/main/scripts/Install-AGIPDMMigrator.ps1

Documentation
-------------
Full documentation available at:
https://github.com/jwraynor/AGI.PDM-Migrator/tree/main/docs

Requirements
------------
- Windows 10/11 or Windows Server 2016+
- .NET 8.0 Runtime
- SolidWorks PDM Client installed
- Administrator privileges
"@ | Out-File -FilePath "release\README.txt" -Encoding UTF8

# Create zip
$zipName = "AGI-PDM-Migrator-v$Version.zip"
Compress-Archive -Path "release/*" -DestinationPath $zipName -Force

# Also create standalone exe
Copy-Item "AGI-PDM/bin/Release/net8.0/win-x64/publish/AGI-PDM.exe" "AGI-PDM-v$Version.exe"

# Clean up
Remove-Item -Path "release" -Recurse -Force

Write-Host "`nRelease artifacts created:" -ForegroundColor Green
Write-Host "  - $zipName (Package with config)" -ForegroundColor Yellow
Write-Host "  - AGI-PDM-v$Version.exe (Standalone)" -ForegroundColor Yellow

$zipSize = [math]::Round((Get-Item $zipName).Length / 1KB, 2)
$exeSize = [math]::Round((Get-Item "AGI-PDM-v$Version.exe").Length / 1MB, 2)

Write-Host "`nFile sizes:" -ForegroundColor Cyan
Write-Host "  - Zip: $zipSize KB" -ForegroundColor Gray
Write-Host "  - Exe: $exeSize MB" -ForegroundColor Gray

Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "1. Test the executable locally"
Write-Host "2. Create and push a git tag: git tag v$Version && git push origin v$Version"
Write-Host "3. GitHub Actions will automatically create the release"