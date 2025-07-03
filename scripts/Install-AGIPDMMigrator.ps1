<#
.SYNOPSIS
    Downloads and runs AGI PDM Migrator from GitHub release
.DESCRIPTION
    Bootstrap script that downloads the latest release and executes the migration tool
#>

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Create temp directory
$tempPath = Join-Path $env:TEMP "AGI-PDM-Migrator-$(Get-Random)"
New-Item -ItemType Directory -Path $tempPath -Force | Out-Null

try {
    # Get latest release
    Write-Host "Downloading AGI PDM Migrator..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/jwraynor/AGI.PDM-Migrator/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -like "*AGI-PDM-Migrator*.zip" } | Select-Object -First 1
    
    if (-not $asset) {
        throw "No release asset found"
    }
    
    # Download
    $zipPath = Join-Path $tempPath "migrator.zip"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath
    
    # Extract
    Expand-Archive -Path $zipPath -DestinationPath $tempPath -Force
    
    # Find and run executable
    $exePath = Get-ChildItem -Path $tempPath -Recurse -Filter "AGI-PDM.exe" | Select-Object -First 1
    if (-not $exePath) {
        throw "AGI-PDM.exe not found in release"
    }
    
    # Change to exe directory and run
    Push-Location $exePath.DirectoryName
    try {
        & $exePath.FullName
        exit $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Error $_
    exit -1
}
finally {
    # Cleanup
    if (Test-Path $tempPath) {
        Remove-Item $tempPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}