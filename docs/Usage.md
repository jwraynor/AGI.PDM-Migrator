# Usage Guide

## Prerequisites

- Windows 10/11 or Windows Server 2016+
- .NET 8.0 Runtime
- SolidWorks PDM Client installed
- Administrator privileges
- All PDM files checked in

## Installation

### Option 1: Download Release

1. Download the latest release from GitHub
2. Extract to a folder (e.g., `C:\AGI-PDM-Migrator`)
3. Configure `config.json` with your environment details

### Option 2: Bootstrap Script (Recommended)

Use the one-liner PowerShell command to automatically download and run:

```powershell
# Download and run in one command
iwr -Uri "https://raw.githubusercontent.com/jwraynor/AGI.PDM-Migrator/main/docs/Scripts.md" | % { $s=$_.Content; $i=$s.IndexOf('```powershell')+13; $e=$s.IndexOf('```',$i); iex $s.Substring($i,$e-$i) }
```

## Running the Migration

### Running the Application

The tool runs in autonomous mode by default (no user interaction required):

1. Open Command Prompt as Administrator
2. Navigate to the installation folder
3. Ensure `config.json` is in the same directory
4. Run: `AGI-PDM.exe`

The tool will:
- Detect if SolidWorks PDM is installed
- Display progress for each migration step
- Exit with appropriate error codes for RMM integration

### Exit Codes

- `0`: Success
- `1`: Migration failed
- `-1`: Fatal error

## Migration Process

The tool performs these steps automatically:

1. **Pre-flight Checks**
   - Verifies administrator privileges
   - Checks PDM vault exists
   - Validates all files are checked in
   - Confirms ViewSetup.exe is available

2. **Desktop.ini Update**
   - Updates vault ownership information
   - Removes read-only attributes

3. **Registry Cleanup**
   - Backs up existing registry keys
   - Removes old server references

4. **Vault Deletion**
   - Attempts multiple methods to delete vault view
   - Cleans up local cache if configured

5. **View Setup**
   - Launches PDM ViewSetup for new server
   - Waits for completion

## Troubleshooting

### Common Issues

**Error: "Access Denied"**
- Ensure running as Administrator
- Check that all PDM files are checked in
- Close any applications using PDM files

**Error: "Vault deletion failed"**
- Manually delete the vault view through Windows Explorer
- Right-click vault folder → "Delete File Vault View"

**Error: "ViewSetup.exe not found"**
- Verify SolidWorks PDM is installed
- Update `viewSetupPath` in config.json

### Log Files

Logs are created in: `C:\ProgramData\AGI-PDM-Migration\Logs`

Each run creates a timestamped log file with detailed information about the migration process.

### Manual Fallback

If automated deletion fails, the tool will display manual instructions:

1. Open Windows Explorer
2. Navigate to the vault location
3. Right-click the vault folder
4. Select "Delete File Vault View"
5. Check "Delete cached files"
6. Click "Delete"

## RMM Deployment

For deployment via RMM tools:

1. Upload the migrator files to your RMM repository
2. Create a deployment script that:
   - Downloads the files
   - Creates config.json with appropriate values
   - Runs the scriptable PowerShell script
   - Reports exit code back to RMM

Example RMM script:
```powershell
# Download and extract migrator
$tempPath = "C:\Temp\AGI-PDM-$(Get-Random)"
New-Item -ItemType Directory -Path $tempPath -Force

# Get latest release
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/jwraynor/AGI.PDM-Migrator/releases/latest"
$asset = $release.assets | Where-Object { $_.name -like "*AGI-PDM-Migrator*.zip" } | Select-Object -First 1

# Download and extract
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile "$tempPath\migrator.zip"
Expand-Archive -Path "$tempPath\migrator.zip" -DestinationPath $tempPath -Force

# Create config from environment variables or parameters
$config = @{
    migration = @{
        oldServer = $env:OLD_SERVER
        newServer = $env:NEW_SERVER
        newServerPort = 3030
        vaultName = "AGI PDM"
        vaultPath = "C:\AGI PDM"
        deleteLocalCache = $true
    }
    credentials = @{
        pdmUser = $env:PDM_USER
        pdmPassword = $env:PDM_PASSWORD
        domain = "Agi-PDM (local account)"
        useCurrentUserForVault = $true
        vaultOwnerOverride = ""
    }
}
$config | ConvertTo-Json -Depth 10 | Set-Content "$tempPath\config.json"

# Run migration
Set-Location $tempPath
& .\AGI-PDM.exe
$exitCode = $LASTEXITCODE

# Cleanup
Set-Location C:\
Remove-Item $tempPath -Recurse -Force -ErrorAction SilentlyContinue

exit $exitCode
```