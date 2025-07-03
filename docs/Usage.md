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

### Option 2: Bootstrap Script

Use the PowerShell bootstrap script to automatically download and run:

```powershell
# Download and run the bootstrap script
.\Install-AGIPDMMigrator.ps1
```

## Running the Migration

### Interactive Mode

For manual execution with prompts:

1. Open Command Prompt as Administrator
2. Navigate to the installation folder
3. Run: `AGI-PDM.exe`

### Autonomous Mode (RMM)

For unattended execution:

1. Place `config.json` in the execution directory
2. Run the scriptable version:
   ```powershell
   .\RunMigration-Scriptable.ps1
   ```

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
# Download migrator
Invoke-WebRequest -Uri "https://github.com/jwraynor/AGI.PDM-Migrator/releases/latest/download/AGI-PDM-Migrator.zip" -OutFile "migrator.zip"
Expand-Archive -Path "migrator.zip" -DestinationPath "C:\Temp\AGI-PDM"

# Create config
$config = @{
    migration = @{
        oldServer = $env:OLD_SERVER
        newServer = $env:NEW_SERVER
        # ... other settings
    }
}
$config | ConvertTo-Json -Depth 10 | Set-Content "C:\Temp\AGI-PDM\config.json"

# Run migration
Set-Location "C:\Temp\AGI-PDM"
$result = & .\RunMigration-Scriptable.ps1
exit $LASTEXITCODE
```