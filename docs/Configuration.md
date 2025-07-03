# Configuration Guide

The AGI PDM Migrator uses a JSON configuration file to control its behavior. If no `config.json` is found in the current directory, a default configuration will be created automatically.

## Configuration File Format

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
    "vaultOwnerOverride": ""
  },
  "registryKeys": {
    "primary": "HKEY_LOCAL_MACHINE\\Software\\SolidWorks\\Applications\\PDMWorks Enterprise\\Databases",
    "wow64": "HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\SolidWorks\\Applications\\PDMWorks Enterprise\\Databases"
  },
  "settings": {
    "verifyCheckedIn": true,
    "backupRegistry": true,
    "requireAdminRights": true,
    "autoRestartAsAdmin": true,
    "viewSetupPath": "C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS PDM\\ViewSetup.exe"
  },
  "logging": {
    "logPath": "C:\\ProgramData\\AGI-PDM-Migration\\Logs",
    "logLevel": "Debug",
    "createDetailedLog": true
  }
}
```

## Configuration Sections

### Migration Settings

- **oldServer**: The current PDM server name to migrate from
- **newServer**: The new PDM server name to migrate to
- **newServerPort**: Port number for the new server (default: 3030)
- **vaultName**: Name of the PDM vault
- **vaultPath**: Local path to the vault view
- **deleteLocalCache**: Whether to delete local cached files (recommended: true)

### Credentials

- **pdmUser**: PDM administrator username
- **pdmPassword**: PDM administrator password (can be base64 encoded)
- **domain**: Domain or computer name for authentication
- **useCurrentUserForVault**: Use current Windows user for vault ownership
- **vaultOwnerOverride**: Optional: specify a different user for vault ownership

### Registry Keys

- **primary**: Primary registry path for PDM databases
- **wow64**: 64-bit registry path for PDM databases

These paths are standard for SolidWorks PDM and typically don't need to be changed.

### Settings

- **verifyCheckedIn**: Check that all files are checked in before migration
- **backupRegistry**: Create backup of registry keys before deletion
- **requireAdminRights**: Ensure the tool runs with administrator privileges
- **autoRestartAsAdmin**: Automatically restart with admin rights if needed
- **viewSetupPath**: Path to PDM ViewSetup.exe

### Logging

- **logPath**: Directory where log files will be created
- **logLevel**: Logging detail level (Debug, Information, Warning, Error)
- **createDetailedLog**: Create verbose logs for troubleshooting

## Password Encoding

Passwords can be stored in plain text or base64 encoded. To encode a password:

```powershell
[Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("YourPassword"))
```

## Environment-Specific Configurations

For different environments, create separate config files:
- `config-dev.json` - Development environment
- `config-prod.json` - Production environment

Then specify the config file when running:
```powershell
Copy-Item config-prod.json config.json
.\AGI-PDM.exe
```