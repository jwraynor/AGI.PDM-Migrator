# AGI PDM Server Migration Tool

This tool automates the process of migrating SolidWorks PDM vault connections from one server to another (specifically from AGISW2 to Agi-PDM).

## Overview

The migration process involves several steps that are typically performed manually:
1. Modifying the desktop.ini file to update the AttachedBy attribute
2. Deleting registry keys that reference the old vault
3. Deleting the existing vault view from the file system
4. Running View Setup to connect to the new server
5. Authenticating with new credentials

This tool automates these steps while providing safety checks, logging, and error handling.

## Prerequisites

- Windows operating system
- SolidWorks PDM must be installed
- Administrator privileges
- All files must be checked in (no local/checked-out files)
- Network connectivity to the new PDM server (AGI network or VPN)
- .NET 8.0 runtime

### PDM API Integration

The tool uses the SolidWorks PDM API (EPDM.Interop.epdm.dll) for vault operations. The project is configured to:
- Use the DLL from the SolidWorks installation directory when available (production)
- Fall back to NuGet package for development environments without PDM installed
- The PDM API is primarily used for vault detection and registry cleanup

## Configuration

Edit the `config.json` file to match your environment:

```json
{
  "migration": {
    "oldServer": "AGISW2",
    "newServer": "Agi-PDM",
    "newServerPort": 3030,
    "vaultName": "AGI PDM",
    "vaultPath": "C:\\AGI PDM",
    "deleteLocalCache": true
  },
  "credentials": {
    "pdmUser": "PDM-user",
    "pdmPassword": "YourPasswordHere",
    "domain": "Agi-PDM (local account)",
    "useCurrentUserForVault": true,
    "vaultOwnerOverride": ""  // Optional: specify exact user like "DOMAIN\\username"
  }
}
```

## Usage

1. Ensure all files are checked in to the vault
2. Close all SolidWorks and PDM applications
3. Run the application as Administrator:
   ```
   AGI-PDM.exe
   ```
4. Follow the on-screen prompts
5. When View Setup launches, follow the manual steps to complete the connection

## Features

### Safety Features
- Pre-flight checks verify system state before making changes
- User confirmation required before destructive operations
- Registry key backup before deletion
- Comprehensive logging for troubleshooting
- Option to continue despite individual step failures

### Automation
- Automatic detection of actual logged-in user (even when running as admin)
- Desktop.ini AttachedBy attribute updated with correct user
- Registry key deletion for both 32-bit and 64-bit installations
- **Advanced process handle detection and termination**:
  - Uses Windows NT APIs to enumerate ALL system handles (same as Resource Monitor)
  - Finds any process with handles to vault folder (WaveSvc, RtkAudService64, OneDrive, etc.)
  - Properly stops Windows services instead of killing them
  - Automatically restarts services after vault deletion
  - Uses multiple fallback detection methods (handle.exe, Restart Manager API, PowerShell)
  - Intelligently handles Explorer windows and restarts Explorer if needed
  - Skips critical system processes for safety
- Multiple methods attempted for vault view deletion:
  - Removes special PDM attributes and desktop.ini first
  - Kills processes with handles to enable deletion
  - Direct deletion after handle release
  - PDM API detection and cleanup tools
  - ViewSetup.exe command line deletion
  - PDM shell deletion using Windows API
  - ConisioAdmin.exe deletion
  - Explorer context menu automation
  - PowerShell-based deletion with Explorer window closing
  - Targeted deletion of problematic subdirectories (Logs, Manufacturing)
- Automatic process termination for EdmServer, ConisioAdmin, Conisio, SLDWORKS
- PDM registry cleanup tools integration
- Manual deletion instructions only as last resort
- View Setup launch with configuration

### Resilience
- Handles various error conditions gracefully
- Skips non-critical steps when files are missing (e.g., desktop.ini)
- Continues migration even with partially deleted vaults
- Provides detailed error messages
- Creates instruction files for manual completion if needed
- Comprehensive logging to troubleshoot issues
- Clear status reporting showing skipped vs failed steps

## Logging

Logs are created in: `C:\ProgramData\AGI-PDM-Migration\Logs`

Each run creates a dated log file with detailed information about the migration process.

## Troubleshooting

### Common Issues

1. **"Application must be run with administrator privileges"**
   - Right-click the executable and select "Run as administrator"

2. **"Cannot reach new PDM server"**
   - Ensure you're connected to the AGI network or VPN
   - Verify the server name in config.json

3. **"View Setup executable not found"**
   - Verify PDM is installed
   - Update the viewSetupPath in config.json if installed in a non-standard location

4. **"Found potential checked-out files"**
   - Check in all files before running the migration
   - The tool lists files that appear to be checked out

5. **"All deletion methods failed for vault view"**
   - This is common due to PDM's special folder handling
   - Follow the manual deletion instructions provided
   - Right-click the vault folder and select "Delete File Vault View"
   - Make sure to check "Delete the cached file vault files and folders"

6. **"Wrong user in desktop.ini"**
   - The tool automatically detects the actual logged-in user
   - Even when running as admin, it finds the correct user account
   - Uses multiple methods including WMI and Explorer process owner
   - If detection shows wrong user (e.g., "su-jraynor" instead of "jraynor"):
     - Set "vaultOwnerOverride" in config.json to the correct user
     - Format: "DOMAIN\\username" (e.g., "agisign\\jraynor")

7. **"desktop.ini file not found"**
   - This occurs if the vault was partially deleted in a previous run
   - The tool now handles empty vault directories automatically
   - If the directory contains files but no desktop.ini, manual cleanup may be needed

### Manual Fallback

If the automated process fails at any step, you can complete the migration manually by following the original procedure document. The tool will indicate which steps were completed successfully.

## Security Notes

- Credentials in config.json can be base64 encoded for basic obfuscation
- The tool requires administrator privileges to modify system settings
- Registry backups are created but not automatically restored

## Building from Source

Requirements:
- .NET 8.0 SDK
- Visual Studio 2022 or JetBrains Rider

Build command:
```
dotnet build -c Release
```

## License

Internal use only - AGI proprietary software