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
Write-Host "Publishing release build..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
Set-Location ".."

# Create release package
Write-Host "Creating release package..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path "release" -Force | Out-Null
Copy-Item "AGI-PDM/bin/Release/net8.0/win-x64/publish/AGI-PDM.exe" "release/"
Copy-Item "AGI-PDM/config.json" "release/"
Copy-Item "README.md" "release/"
Copy-Item "scripts/Install-AGIPDMMigrator.ps1" "release/"

# Create docs folder in release
New-Item -ItemType Directory -Path "release/docs" -Force | Out-Null
Copy-Item "docs/*" "release/docs/" -Recurse

# Create zip
$zipName = "AGI-PDM-Migrator-v$Version.zip"
Compress-Archive -Path "release/*" -DestinationPath $zipName -Force

# Clean up
Remove-Item -Path "release" -Recurse -Force

Write-Host "`nRelease package created: $zipName" -ForegroundColor Green
Write-Host "File size: $([math]::Round((Get-Item $zipName).Length / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "1. Go to https://github.com/jwraynor/AGI.PDM-Migrator/releases/new"
Write-Host "2. Create tag: v$Version"
Write-Host "3. Upload $zipName"
Write-Host "4. Publish release"