# AGI PDM Migrator Scripts

## Bootstrap Installation Script

This PowerShell script downloads and runs the latest release of AGI PDM Migrator from GitHub. It's designed for automated deployment through RMM tools or manual execution.

### Script Content

```powershell
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
```

### Usage

#### Direct Download and Run

```powershell
# Download the script
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/jwraynor/AGI.PDM-Migrator/main/docs/Scripts.md" -OutFile "Install-AGIPDMMigrator.ps1"

# Extract just the PowerShell code from the markdown
$content = Get-Content "Install-AGIPDMMigrator.ps1" -Raw
$start = $content.IndexOf('```powershell') + 13
$end = $content.IndexOf('```', $start)
$script = $content.Substring($start, $end - $start)
$script | Out-File "Install-AGIPDMMigrator.ps1" -Encoding UTF8

# Run it
.\Install-AGIPDMMigrator.ps1
```

#### One-Liner Execution

For RMM deployment or quick execution:

```powershell
# This downloads and executes the script in one command
$scriptUrl = "https://raw.githubusercontent.com/jwraynor/AGI.PDM-Migrator/main/docs/Scripts.md"
$scriptContent = (Invoke-WebRequest -Uri $scriptUrl).Content
$start = $scriptContent.IndexOf('```powershell') + 13
$end = $scriptContent.IndexOf('```', $start)
$script = $scriptContent.Substring($start, $end - $start)
Invoke-Expression $script
```

### What the Script Does

1. **Creates Temporary Directory**: Uses a random name in the temp folder to avoid conflicts
2. **Downloads Latest Release**: Queries GitHub API for the latest release assets
3. **Extracts Package**: Unzips the downloaded release package
4. **Runs AGI-PDM.exe**: Executes the migration tool from the extracted location
5. **Cleans Up**: Removes temporary files after execution

### Requirements

- PowerShell 5.0 or later
- Internet access to GitHub
- Administrator privileges (required by AGI-PDM.exe)

### Error Handling

- The script uses `$ErrorActionPreference = "Stop"` to halt on any errors
- Exit codes are preserved from the AGI-PDM.exe execution
- Temporary files are cleaned up even if errors occur
- Error messages are written to stderr with proper exit codes

### Security Notes

- The script downloads from the official GitHub repository only
- Uses HTTPS for all downloads
- Validates that the executable exists before running
- Runs with the same privileges as the calling process (should be administrator)