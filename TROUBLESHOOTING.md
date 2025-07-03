# AGI-PDM Migration Tool - Troubleshooting Guide

## Vault Deletion Issues

### Recent Enhancements (June 2025)

The vault deletion functionality has been enhanced with:

1. **Automatic process termination** - Kills EdmServer, ConisioAdmin, and other PDM processes
2. **Better diagnostic logging** - The tool now checks and logs which directories might be locked
3. **Process detection** - Identifies common PDM-related processes that might be holding locks
4. **Explorer window closing** - Automatically attempts to close any Explorer windows showing the vault path
5. **Enhanced PowerShell deletion** - More aggressive deletion approach with better error reporting
6. **Targeted subdirectory deletion** - Specifically targets problematic directories like "Logs" and "Manufacturing"
7. **Multiple fallback methods** - Includes rename-and-delete strategy for stubborn directories

### Common Vault Deletion Errors

#### "The process cannot access the file because it is being used by another process"

This typically occurs when:
- Windows Explorer has the vault directory open
- SolidWorks or PDM processes are still running
- The "Logs" directory contains active log files
- Antivirus software is scanning the directory

**Solutions:**
1. Close all Explorer windows
2. Exit SolidWorks and PDM applications
3. Check Task Manager for these processes:
   - EdmServer.exe
   - ConisioAdmin.exe
   - Conisio.exe
   - SLDWORKS.exe
   - ViewSetup.exe

#### Partially Deleted Vault

If the vault contains only empty directories (like "Logs" and "Manufacturing\Library Components"), this indicates a partial deletion from a previous attempt.

**What the tool does:**
- Detects this state during pre-flight checks
- Attempts multiple deletion methods
- Provides detailed logging about which directories are problematic

### Manual Deletion Methods

If automated deletion fails:

1. **Method 1 - PDM Context Menu (Recommended)**
   - Open Windows Explorer
   - Navigate to C:\
   - Right-click "AGI PDM" folder
   - Select "Delete File Vault View"
   - Check "Delete the cached file vault files and folders"
   - Click Delete

2. **Method 2 - Command Line (Admin)**
   ```cmd
   takeown /f "C:\AGI PDM" /r /d y
   icacls "C:\AGI PDM" /grant administrators:F /t
   rd /s /q "C:\AGI PDM"
   ```

3. **Method 3 - Safe Mode**
   - Restart Windows in Safe Mode
   - Delete the directory normally
   - Restart in normal mode

### Debugging Vault Deletion

The enhanced logging now provides:
- List of all directories in the vault
- Detection of locked directories
- Running PDM-related processes
- PowerShell script output
- Detailed error messages from each deletion attempt

Check the log file at: `C:\ProgramData\AGI-PDM-Migration\Logs\agi-pdm-YYYY-MM-DD.log`

### Prevention

To avoid deletion issues:
1. Always close all SolidWorks and PDM applications before migration
2. Close any Explorer windows showing the vault
3. Ensure no files are checked out
4. Run the tool as Administrator
5. Temporarily disable antivirus real-time scanning

### Known Issues

1. **UNC Path Warning**: The message about "UNC paths are not supported" can be ignored - it's from running the tool from a network location and doesn't affect vault deletion

2. **Locked Subdirectories**: Empty subdirectories like "Logs" and "Manufacturing\Library Components" are commonly locked by Windows indexing or thumbnail generation

3. **Junction Points**: Some PDM vaults may be created as junction points, which require special handling (the tool attempts this automatically)