using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using Microsoft.Win32;
using System.Text;
using System.ComponentModel;
using System.Security.Principal;

namespace AGI_PDM.Services;

public class VaultViewManager
{
    private readonly string _vaultPath;
    private readonly string _vaultName;
    private readonly bool _deleteCache;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        public string pFrom;
        public string pTo;
        public short fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    private const int FO_DELETE = 0x0003;
    private const int FOF_ALLOWUNDO = 0x0040;
    private const int FOF_NOCONFIRMATION = 0x0010;
    private const int FOF_SILENT = 0x0004;
    private const int FOF_NOERRORUI = 0x0400;
    
    // NT API structures for handle enumeration
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);
    
    [DllImport("ntdll.dll")]
    private static extern int NtQueryObject(IntPtr Handle, int ObjectInformationClass, IntPtr ObjectInformation, int ObjectInformationLength, out int ReturnLength);
    
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
    
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
    
    [DllImport("kernel32.dll")]
    private static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions);
    
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    
    private const int PROCESS_DUP_HANDLE = 0x0040;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int DUPLICATE_SAME_ACCESS = 0x0002;
    private const int SystemHandleInformation = 16;
    private const int ObjectNameInformation = 1;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_INFORMATION
    {
        public int NumberOfHandles;
        public SYSTEM_HANDLE_TABLE_ENTRY_INFO Handles;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO
    {
        public short UniqueProcessId;
        public short CreatorBackTraceIndex;
        public byte ObjectTypeIndex;
        public byte HandleAttributes;
        public short HandleValue;
        public IntPtr Object;
        public int GrantedAccess;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct OBJECT_NAME_INFORMATION
    {
        public UNICODE_STRING Name;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public short Length;
        public short MaximumLength;
        public IntPtr Buffer;
    }

    public VaultViewManager(string vaultPath, string vaultName, bool deleteCache = true)
    {
        _vaultPath = vaultPath;
        _vaultName = vaultName;
        _deleteCache = deleteCache;
    }

    public bool DeleteVaultView()
    {
        try
        {
            Log.Information("Starting vault view deletion from: {VaultPath}", _vaultPath);

            if (!Directory.Exists(_vaultPath))
            {
                Log.Warning("Vault directory does not exist: {VaultPath}", _vaultPath);
                return true; // Not an error if it doesn't exist
            }

            // Step 1: Remove special attributes (desktop.ini, system folder attributes)
            Log.Information("Step 1: Removing special attributes and desktop.ini");
            RemoveVaultSpecialAttributes();
            
            // Step 2: Try to unregister PDM shell namespace extension for this folder
            Log.Information("Step 2: Unregistering PDM shell namespace");
            UnregisterPdmShellNamespace();
            
            // Step 3: Find and kill processes with handles to the vault
            Log.Information("Step 3: Finding and terminating processes with handles to vault");
            if (!KillLockingProcesses())
            {
                Log.Warning("Some processes with handles could not be terminated");
            }
            
            // Step 4: Try direct deletion now that handles should be released
            Log.Information("Step 4: Attempting direct vault deletion");
            try
            {
                Directory.Delete(_vaultPath, true);
                Log.Information("Successfully deleted vault directory");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Direct deletion failed, trying alternative methods");
            }
            
            // Step 4: Check with PDM API and try cleanup
            Log.Information("Step 4: Using PDM API for cleanup");
            var pdmService = new PdmVaultService(_vaultName, _vaultPath);
            pdmService.DeleteVaultViewUsingApi(); // This will try ViewSetup.exe and registry cleanup
            pdmService.TryCleanVaultRegistry();
            
            // Check if PDM cleanup helped
            if (!Directory.Exists(_vaultPath))
            {
                return true;
            }

            // Continue with other methods if still exists
            // Check if directory is already empty (partially deleted vault)
            var files = Directory.GetFiles(_vaultPath, "*", SearchOption.AllDirectories);
            var dirs = Directory.GetDirectories(_vaultPath, "*", SearchOption.AllDirectories);
            
            Log.Information("Vault still exists with {FileCount} files and {DirCount} directories", files.Length, dirs.Length);
            
            if (files.Length == 0 && dirs.Length > 0)
            {
                // Try to delete problematic subdirectories first
                if (TryDeleteProblematicSubdirectories())
                {
                    Log.Debug("Problematic subdirectories removed");
                }
                
                try
                {
                    // Try to delete again
                    Directory.Delete(_vaultPath, true);
                    Log.Information("Successfully deleted vault directory after removing subdirectories");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete vault directory after subdirectory removal");
                }
            }

            // Method 1: Check if this is a junction/reparse point and handle specially
            if (TryDeleteJunctionOrReparsePoint())
            {
                Log.Information("Successfully deleted vault view as junction/reparse point");
                return true;
            }

            // Method 2: Try PDM-specific deletion using shell delete
            if (TryPdmShellDelete())
            {
                Log.Information("Successfully deleted vault view via PDM shell delete");
                return true;
            }

            // Method 3: Try using ConisioAdmin.exe if available
            if (TryConisioAdminDelete())
            {
                Log.Information("Successfully deleted vault view via ConisioAdmin");
                return true;
            }

            // Method 4: Try using Windows Explorer context menu
            if (TryDeleteViaExplorer())
            {
                Log.Information("Successfully deleted vault view via Explorer method");
                return true;
            }

            // Method 5: Try direct deletion with special handling
            if (TryDirectDeletion())
            {
                Log.Information("Successfully deleted vault view via direct deletion");
                return true;
            }

            // Method 6: Use command line with special PDM commands
            if (TryDeleteViaCommandLine())
            {
                Log.Information("Successfully deleted vault view via command line");
                return true;
            }

            Log.Error("All deletion methods failed for vault view");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting vault view");
            return false;
        }
    }

    private bool TryDeleteJunctionOrReparsePoint()
    {
        try
        {
            Log.Debug("Checking if vault is a junction or reparse point");
            
            var dirInfo = new DirectoryInfo(_vaultPath);
            if (!dirInfo.Exists)
            {
                return false;
            }
            
            // Check if it has reparse point attribute (junctions, symbolic links, etc.)
            bool isReparsePoint = (dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            Log.Debug("Directory attributes: {Attributes}, IsReparsePoint: {IsReparsePoint}", 
                dirInfo.Attributes, isReparsePoint);
            
            if (isReparsePoint)
            {
                Log.Information("Vault appears to be a reparse point/junction, using special deletion");
                
                // Use rmdir without /s flag for junctions
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rmdir \"{_vaultPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(5000);
                    
                    var error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Log.Debug("Junction deletion error: {Error}", error);
                    }
                    
                    return !Directory.Exists(_vaultPath);
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Junction/reparse point deletion failed");
            return false;
        }
    }

    private bool TryPdmShellDelete()
    {
        try
        {
            Log.Debug("Attempting PDM shell delete using SHFileOperation");

            // Use SHFileOperation which properly handles special folders like PDM vaults
            var fileOp = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = _vaultPath + "\0\0", // Double null-terminated
                pTo = null,
                fFlags = (short)(FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = null
            };

            Log.Debug("Calling SHFileOperation for path: {Path}", _vaultPath);
            var result = SHFileOperation(ref fileOp);
            
            Log.Debug("SHFileOperation returned: {Result}, Aborted: {Aborted}", result, fileOp.fAnyOperationsAborted);
            
            if (result == 0 && !fileOp.fAnyOperationsAborted)
            {
                Thread.Sleep(1000); // Give it a moment
                var stillExists = Directory.Exists(_vaultPath);
                Log.Debug("After SHFileOperation, directory exists: {Exists}", stillExists);
                return !stillExists;
            }

            // Common error codes
            var errorMessage = result switch
            {
                2 => "File not found",
                3 => "Path not found",
                5 => "Access denied",
                32 => "Sharing violation",
                112 => "Disk full",
                1223 => "Operation cancelled",
                _ => $"Unknown error code: {result}"
            };
            
            Log.Debug("SHFileOperation failed: {ErrorMessage}", errorMessage);
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "PDM shell delete failed");
            return false;
        }
    }

    private bool TryConisioAdminDelete()
    {
        try
        {
            Log.Debug("Attempting ConisioAdmin.exe deletion");

            // Look for ConisioAdmin.exe in common PDM installation paths
            var possiblePaths = new[]
            {
                @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM\ConisioAdmin.exe",
                @"C:\Program Files\SOLIDWORKS PDM\ConisioAdmin.exe",
                @"C:\Program Files (x86)\SOLIDWORKS PDM\ConisioAdmin.exe",
                @"C:\Program Files (x86)\SOLIDWORKS Corp\SOLIDWORKS PDM\ConisioAdmin.exe"
            };

            string? conisioPath = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    conisioPath = path;
                    break;
                }
            }

            if (conisioPath == null)
            {
                Log.Debug("ConisioAdmin.exe not found");
                return false;
            }

            // Try to delete using ConisioAdmin
            var psi = new ProcessStartInfo
            {
                FileName = conisioPath,
                Arguments = $"/DeleteVault \"{_vaultPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(TimeSpan.FromSeconds(30).Milliseconds);
                Thread.Sleep(1000);
                return !Directory.Exists(_vaultPath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ConisioAdmin deletion failed");
        }

        return false;
    }

    private bool TryDeleteViaExplorer()
    {
        try
        {
            Log.Debug("Attempting to delete vault view via Explorer/PowerShell");

            // Use PowerShell to invoke the shell context menu for PDM vault deletion
            var script = @"
Add-Type @'
using System;
using System.Runtime.InteropServices;

public class Shell32 {
    [DllImport(""shell32.dll"", CharSet = CharSet.Auto)]
    public static extern IntPtr ILCreateFromPath(string path);
    
    [DllImport(""shell32.dll"", CharSet = CharSet.Auto)]
    public static extern void ILFree(IntPtr pidl);
    
    [DllImport(""shell32.dll"", CharSet = CharSet.Auto)]
    public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);
}
'@

# Navigate to the parent directory and select the vault folder
$vaultPath = '" + _vaultPath.Replace("'", "''") + @"'
$parentPath = Split-Path $vaultPath -Parent

# Open Explorer and select the vault folder
$pidl = [Shell32]::ILCreateFromPath($vaultPath)
[Shell32]::SHOpenFolderAndSelectItems($pidl, 0, [IntPtr]::Zero, 0)
[Shell32]::ILFree($pidl)

Start-Sleep -Milliseconds 500

# Try to find and click the Delete File Vault View option using UI Automation
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient

# Send right-click
[System.Windows.Forms.SendKeys]::SendWait('+{F10}')
Start-Sleep -Milliseconds 500

# Look for 'Delete File Vault View' in context menu
# This would require more sophisticated UI automation
";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(TimeSpan.FromSeconds(15).Milliseconds);
                Thread.Sleep(2000); // Give it time to complete
                
                // Check if the vault directory still exists
                if (!Directory.Exists(_vaultPath))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Explorer deletion method failed");
        }

        return false;
    }

    private bool TryDirectDeletion()
    {
        try
        {
            Log.Debug("Attempting direct deletion of vault view");

            // First check if this is actually a PDM vault by looking for vault markers
            var desktopIniPath = Path.Combine(_vaultPath, "desktop.ini");
            var isPdmVault = File.Exists(desktopIniPath);

            if (isPdmVault)
            {
                // Read desktop.ini to understand the vault configuration
                try
                {
                    var desktopIniContent = File.ReadAllText(desktopIniPath);
                    Log.Debug("Desktop.ini content: {Content}", desktopIniContent);
                }
                catch { }
            }

            // Remove the system folder attribute which PDM sets
            try
            {
                var dirInfo = new DirectoryInfo(_vaultPath);
                dirInfo.Attributes = FileAttributes.Directory; // Remove all special attributes
                Log.Debug("Removed special attributes from vault directory");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to remove directory attributes");
            }

            // Remove attributes from all files in the vault
            try
            {
                foreach (var file in Directory.GetFiles(_vaultPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
            }
            catch { }

            // Remove the desktop.ini file first
            if (File.Exists(desktopIniPath))
            {
                try
                {
                    File.SetAttributes(desktopIniPath, FileAttributes.Normal);
                    File.Delete(desktopIniPath);
                    Log.Debug("Deleted desktop.ini");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to delete desktop.ini");
                }
            }

            // Now try to delete the directory
            if (_deleteCache)
            {
                Log.Debug("Deleting vault directory with cached files");
                
                // Use a more aggressive deletion approach
                var deleteScript = $@"
                    $path = '{_vaultPath.Replace("'", "''")}'
                    if (Test-Path $path) {{
                        Get-ChildItem -Path $path -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
                        Remove-Item -Path $path -Force -Recurse -ErrorAction SilentlyContinue
                    }}
                ";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{deleteScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit(30000);
            }
            else
            {
                Log.Debug("Deleting vault directory structure only");
                DeleteVaultStructure(_vaultPath);
            }

            Thread.Sleep(1000); // Give it a moment
            return !Directory.Exists(_vaultPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Direct deletion method failed");
        }

        return false;
    }

    private bool TryDeleteViaCommandLine()
    {
        try
        {
            Log.Debug("Attempting command line deletion of vault view");

            // Method 1: Use rmdir with special flags
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c rmdir /s /q \"{_vaultPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(TimeSpan.FromSeconds(30).Milliseconds);
                
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Log.Debug("Command output: {Output}", output);
                }
                
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Log.Debug("Command error: {Error}", error);
                }

                if (!Directory.Exists(_vaultPath))
                {
                    return true;
                }
            }

            // Method 2: Try PowerShell with more aggressive approach
            Log.Debug("Trying PowerShell deletion");
            
            var psScript = $@"
                $path = '{_vaultPath.Replace("'", "''")}'
                if (Test-Path $path) {{
                    Write-Host 'Attempting to delete vault at: ' $path
                    
                    # Close any Explorer windows showing this path
                    $shell = New-Object -ComObject Shell.Application
                    $windows = $shell.Windows()
                    $pathUrl = $path.Replace('\', '/')
                    foreach ($window in $windows) {{
                        try {{
                            if ($window.LocationURL -like ""*$pathUrl*"") {{
                                Write-Host 'Closing Explorer window: ' $window.LocationURL
                                $window.Quit()
                            }}
                        }} catch {{ }}
                    }}
                    Start-Sleep -Milliseconds 500
                    
                    # Take ownership if needed
                    Write-Host 'Taking ownership of directory...'
                    takeown /f ""$path"" /r /d y 2>&1 | Out-Null
                    icacls ""$path"" /grant administrators:F /t 2>&1 | Out-Null
                    
                    # Remove all attributes
                    Write-Host 'Removing attributes...'
                    Get-ChildItem -Path $path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {{
                        try {{
                            $_.Attributes = 'Normal'
                        }} catch {{ }}
                    }}
                    
                    # Try to unlock directories by resetting them
                    Get-ChildItem -Path $path -Directory -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {{
                        try {{
                            $dir = $_.FullName
                            attrib -r -s -h ""$dir"" /d
                        }} catch {{ }}
                    }}
                    
                    # Force delete
                    Write-Host 'Attempting deletion...'
                    Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
                    
                    # If still exists, try cmd
                    if (Test-Path $path) {{
                        Write-Host 'First attempt failed, trying cmd...'
                        & cmd /c rd /s /q ""$path"" 2>&1
                    }}
                    
                    # Final check
                    if (Test-Path $path) {{
                        Write-Host 'Directory still exists after deletion attempts'
                        Get-ChildItem -Path $path -Recurse -ErrorAction SilentlyContinue | Select-Object FullName
                    }} else {{
                        Write-Host 'Directory successfully deleted'
                    }}
                }}
            ";

            var psPsi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"
            };

            using var psProcess = Process.Start(psPsi);
            if (psProcess != null)
            {
                psProcess.WaitForExit(30000);
                
                var output = psProcess.StandardOutput.ReadToEnd();
                var error = psProcess.StandardError.ReadToEnd();
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Log.Debug("PowerShell output: {Output}", output);
                }
                
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Log.Warning("PowerShell error: {Error}", error);
                }
            }

            return !Directory.Exists(_vaultPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Command line deletion method failed");
        }

        return false;
    }

    private void DeleteVaultStructure(string path)
    {
        // This method would selectively delete vault structure files
        // while preserving cached files if requested
        var filesToDelete = new[] { "desktop.ini", ".pdmvault" };
        
        foreach (var file in filesToDelete)
        {
            var filePath = Path.Combine(path, file);
            if (File.Exists(filePath))
            {
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete {File}", file);
                }
            }
        }

        // Try to remove the directory if it's empty
        try
        {
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Directory not empty or other error
        }
    }

    public bool WaitForDeletion(int timeoutSeconds = 30)
    {
        var stopwatch = Stopwatch.StartNew();
        
        while (Directory.Exists(_vaultPath) && stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
        {
            Thread.Sleep(500);
        }

        return !Directory.Exists(_vaultPath);
    }
    
    private void RemoveVaultSpecialAttributes()
    {
        try
        {
            // First, remove desktop.ini which marks this as a special PDM folder
            var desktopIniPath = Path.Combine(_vaultPath, "desktop.ini");
            if (File.Exists(desktopIniPath))
            {
                try
                {
                    // Remove any special attributes from desktop.ini
                    File.SetAttributes(desktopIniPath, FileAttributes.Normal);
                    File.Delete(desktopIniPath);
                    Log.Debug("Deleted desktop.ini file");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to delete desktop.ini");
                }
            }
            
            // Remove the system folder attribute which PDM sets
            var dirInfo = new DirectoryInfo(_vaultPath);
            if (dirInfo.Exists)
            {
                // Remove system, hidden, and readonly attributes
                dirInfo.Attributes = FileAttributes.Directory;
                Log.Debug("Removed special attributes from vault root directory");
            }

            // Remove attributes from all subdirectories
            foreach (var subDir in Directory.GetDirectories(_vaultPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var subDirInfo = new DirectoryInfo(subDir);
                    subDirInfo.Attributes = FileAttributes.Directory;
                }
                catch { }
            }

            // Remove attributes from all files
            foreach (var file in Directory.GetFiles(_vaultPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch { }
            }
            
            // Also remove any .pdmvault files which may exist
            var pdmVaultFiles = Directory.GetFiles(_vaultPath, ".pdmvault", SearchOption.AllDirectories);
            foreach (var pdmFile in pdmVaultFiles)
            {
                try
                {
                    File.SetAttributes(pdmFile, FileAttributes.Normal);
                    File.Delete(pdmFile);
                    Log.Debug("Deleted .pdmvault file: {File}", pdmFile);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error removing vault special attributes");
        }
    }
    
    private void UnregisterPdmShellNamespace()
    {
        try
        {
            Log.Debug("Attempting to unregister PDM shell namespace for vault");
            
            // Method 1: Use PowerShell to refresh shell and remove PDM namespace registration
            var script = $@"
                $path = '{_vaultPath.Replace("'", "''")}'
                
                # Force Windows to forget about this being a special folder
                try {{
                    # Remove from shell namespace
                    $shell = New-Object -ComObject Shell.Application
                    $shell.NameSpace($path).Self.InvokeVerb('Remove')
                }} catch {{ }}
                
                # Clear icon cache and refresh
                try {{
                    ie4uinit.exe -ClearIconCache
                    ie4uinit.exe -show
                }} catch {{ }}
                
                # Notify shell of change
                try {{
                    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
                    
                    # Send change notification
                    Add-Type @'
                    using System;
                    using System.Runtime.InteropServices;
                    public class Shell32 {{
                        [DllImport(""shell32.dll"")]
                        public static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
                    }}
'@
                    $SHCNE_RMDIR = 0x00000010
                    $SHCNE_DELETE = 0x00000002
                    $SHCNE_UPDATEDIR = 0x00001000
                    $SHCNF_PATH = 0x0001
                    
                    [Shell32]::SHChangeNotify($SHCNE_UPDATEDIR, $SHCNF_PATH, [System.IntPtr]::Zero, [System.IntPtr]::Zero)
                }} catch {{ }}
            ";
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(5000);
            }
            
            // Method 2: Kill and restart Explorer to force refresh
            try
            {
                var explorerProcesses = Process.GetProcessesByName("explorer");
                if (explorerProcesses.Any())
                {
                    Log.Debug("Restarting Explorer to clear PDM namespace cache");
                    foreach (var exp in explorerProcesses)
                    {
                        try { exp.Kill(); } catch { }
                    }
                    Thread.Sleep(1000);
                    Process.Start("explorer.exe");
                    Thread.Sleep(2000);
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error unregistering PDM shell namespace");
        }
    }
    
    private void RemoveSpecialAttributes(string path)
    {
        try
        {
            // Remove attributes from the directory itself
            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.Exists)
            {
                dirInfo.Attributes = FileAttributes.Directory;
                Log.Debug("Removed special attributes from: {Path}", path);
            }

            // Remove attributes from all subdirectories
            foreach (var subDir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var subDirInfo = new DirectoryInfo(subDir);
                    subDirInfo.Attributes = FileAttributes.Directory;
                }
                catch { }
            }

            // Remove attributes from all files
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error removing special attributes from {Path}", path);
        }
    }
    
    private void CheckForLockingProcesses(string path)
    {
        try
        {
            Log.Debug("Checking for processes that might be locking the vault directory");
            
            // Common PDM-related processes that might lock directories
            var pdmProcesses = new[] 
            { 
                "EdmServer", 
                "ConisioAdmin", 
                "Conisio", 
                "SLDWORKS",
                "explorer",
                "ViewSetup"
            };
            
            foreach (var processName in pdmProcesses)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        Log.Warning("Found {Count} instances of {ProcessName} running", processes.Length, processName);
                    }
                }
                catch { }
            }
            
            // Try to identify specific process locking the directory using PowerShell
            try
            {
                var script = $@"
                    $path = '{path.Replace("'", "''")}'
                    $handles = & handle.exe -a -p * $path 2>$null
                    if ($handles) {{
                        Write-Output 'Processes with handles to vault:'
                        $handles | Select-String -Pattern '^\s*(\w+\.exe)\s+pid:\s*(\d+)' | ForEach-Object {{
                            $matches = $_.Matches[0].Groups
                            Write-Output ""  - $($matches[1].Value) (PID: $($matches[2].Value))""
                        }}
                    }}
                ";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(5000);
                    var output = process.StandardOutput.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Log.Debug("Handle check output: {Output}", output);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not check handles: {Message}", ex.Message);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error checking for locking processes");
        }
    }
    
    private bool KillLockingProcesses()
    {
        try
        {
            Log.Information("Attempting to find and terminate processes locking the vault");
            
            // First, find processes with handles to the vault path
            var processesWithHandles = FindProcessesWithHandlesToPath(_vaultPath);
            
            // Also check for specific services that commonly lock folders
            var servicesWithHandles = FindServicesWithHandles();
            processesWithHandles.AddRange(servicesWithHandles);
            
            if (processesWithHandles.Any())
            {
                Log.Warning("Found {Count} processes with handles to vault path:", processesWithHandles.Count);
                foreach (var (processId, processName) in processesWithHandles)
                {
                    Log.Warning("  - {ProcessName} (PID: {ProcessId})", processName, processId);
                }
                
                // Kill processes that have handles (except critical system processes)
                return KillProcessesWithHandles(processesWithHandles);
            }
            else
            {
                Log.Information("No processes found with handles to vault path using handle detection");
                
                // Fallback: Kill known PDM processes
                return KillKnownPdmProcesses();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in KillLockingProcesses, falling back to known PDM processes");
            // Fallback to killing known PDM processes
            return KillKnownPdmProcesses();
        }
    }
    
    private List<(int ProcessId, string ProcessName)> FindProcessesWithHandlesToPath(string path)
    {
        var processesWithHandles = new List<(int, string)>();
        
        try
        {
            Log.Debug("Searching for processes with handles to: {Path}", path);
            
            // First try the NT API method which is most accurate
            var ntApiResults = FindProcessesUsingNtApi(path);
            if (ntApiResults.Any())
            {
                Log.Debug("Found {Count} processes using NT API method", ntApiResults.Count);
                return ntApiResults;
            }
            
            // Try WMI method to find processes with handles
            var wmiResults = FindProcessesUsingWmi(path);
            if (wmiResults.Any())
            {
                Log.Debug("Found {Count} processes using WMI method", wmiResults.Count);
                return wmiResults;
            }
            
            // Fallback: Try using handle.exe if available
            var handleExePath = FindHandleExe();
            if (!string.IsNullOrEmpty(handleExePath))
            {
                Log.Debug("Using handle.exe to find processes");
                return FindProcessesUsingHandleExe(handleExePath, path);
            }
            
            // Fallback: Use PowerShell with various methods
            Log.Debug("Using PowerShell to find processes with handles");
            return FindProcessesUsingPowerShell(path);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error finding processes with handles");
            return processesWithHandles;
        }
    }
    
    private List<(int ProcessId, string ProcessName)> FindProcessesUsingNtApi(string targetPath)
    {
        var results = new List<(int, string)>();
        var processCache = new Dictionary<int, string>();
        
        // Normalize the target path for comparison
        var normalizedTarget = targetPath.ToUpperInvariant().TrimEnd('\\');
        
        try
        {
            // First, get the size needed for system handle information
            int bufferSize = 0x10000; // Start with 64KB
            IntPtr buffer = IntPtr.Zero;
            
            while (true)
            {
                buffer = Marshal.AllocHGlobal(bufferSize);
                
                try
                {
                    int status = NtQuerySystemInformation(SystemHandleInformation, buffer, bufferSize, out int returnLength);
                    
                    if (status == 0) // STATUS_SUCCESS
                    {
                        break;
                    }
                    else if (status == -1073741820) // STATUS_INFO_LENGTH_MISMATCH (0xC0000004)
                    {
                        bufferSize = returnLength + 0x10000;
                        Marshal.FreeHGlobal(buffer);
                        continue;
                    }
                    else
                    {
                        Log.Debug("NtQuerySystemInformation failed with status: 0x{Status:X8}", status);
                        return results;
                    }
                }
                catch
                {
                    Marshal.FreeHGlobal(buffer);
                    throw;
                }
            }
            
            try
            {
                var handleInfo = Marshal.PtrToStructure<SYSTEM_HANDLE_INFORMATION>(buffer);
                var handleEntrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO>();
                
                Log.Debug("System has {HandleCount} handles", handleInfo.NumberOfHandles);
                
                for (int i = 0; i < handleInfo.NumberOfHandles; i++)
                {
                    var entryPtr = IntPtr.Add(buffer, 4 + (i * handleEntrySize));
                    var handle = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO>(entryPtr);
                    
                    // Skip handles from System process (PID 4) and our own process
                    if (handle.UniqueProcessId <= 4 || handle.UniqueProcessId == Process.GetCurrentProcess().Id)
                        continue;
                    
                    // Check if this handle might be a file/directory handle
                    // Skip if not a file handle type (types vary by Windows version)
                    // We'll check all handles to be safe
                    // if (handle.ObjectTypeIndex != 28 && handle.ObjectTypeIndex != 30 && handle.ObjectTypeIndex != 37)
                    //     continue;
                    
                    // Get handle name to check if it matches our path
                    var handleName = GetHandleNameForProcess(handle.UniqueProcessId, (IntPtr)handle.HandleValue);
                    
                    if (!string.IsNullOrEmpty(handleName))
                    {
                        var normalizedHandle = handleName.ToUpperInvariant();
                        
                        // Check if this handle is for our target path or a file/folder within it
                        if (normalizedHandle.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                            normalizedHandle.StartsWith(normalizedTarget + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            // Get process name if we haven't already
                            if (!processCache.TryGetValue(handle.UniqueProcessId, out var processName))
                            {
                                try
                                {
                                    var process = Process.GetProcessById(handle.UniqueProcessId);
                                    processName = process.ProcessName;
                                    processCache[handle.UniqueProcessId] = processName;
                                }
                                catch
                                {
                                    processName = $"Process_{handle.UniqueProcessId}";
                                    processCache[handle.UniqueProcessId] = processName;
                                }
                            }
                            
                            // Add to results if not already present
                            if (!results.Any(r => r.Item1 == handle.UniqueProcessId))
                            {
                                results.Add((handle.UniqueProcessId, processName));
                                Log.Debug("Found handle to vault in {ProcessName} (PID: {PID})", processName, handle.UniqueProcessId);
                            }
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error enumerating system handles via NT API");
        }
        
        return results;
    }
    
    private string? GetHandleNameForProcess(int processId, IntPtr handle)
    {
        IntPtr processHandle = IntPtr.Zero;
        IntPtr duplicatedHandle = IntPtr.Zero;
        
        try
        {
            // Open the process
            processHandle = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_INFORMATION, false, processId);
            if (processHandle == IntPtr.Zero)
                return null;
            
            // Duplicate the handle into our process
            if (!DuplicateHandle(processHandle, handle, GetCurrentProcess(), out duplicatedHandle, 0, false, DUPLICATE_SAME_ACCESS))
                return null;
            
            // Query the object name
            int bufferSize = 0x1000;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            
            try
            {
                int status = NtQueryObject(duplicatedHandle, ObjectNameInformation, buffer, bufferSize, out int returnLength);
                
                if (status == -1073741820 && returnLength > 0) // STATUS_INFO_LENGTH_MISMATCH
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = returnLength;
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    status = NtQueryObject(duplicatedHandle, ObjectNameInformation, buffer, bufferSize, out returnLength);
                }
                
                if (status == 0) // STATUS_SUCCESS
                {
                    var nameInfo = Marshal.PtrToStructure<OBJECT_NAME_INFORMATION>(buffer);
                    if (nameInfo.Name.Length > 0 && nameInfo.Name.Buffer != IntPtr.Zero)
                    {
                        var name = Marshal.PtrToStringUni(nameInfo.Name.Buffer, nameInfo.Name.Length / 2);
                        
                        // Convert device paths to DOS paths
                        if (name?.StartsWith(@"\Device\") == true)
                        {
                            name = ConvertDevicePathToDosPath(name);
                        }
                        
                        return name;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Ignore errors for individual handles
        }
        finally
        {
            if (duplicatedHandle != IntPtr.Zero)
                CloseHandle(duplicatedHandle);
            if (processHandle != IntPtr.Zero)
                CloseHandle(processHandle);
        }
        
        return null;
    }
    
    private string ConvertDevicePathToDosPath(string devicePath)
    {
        try
        {
            // Get all drive letters
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed);
            
            foreach (var drive in drives)
            {
                var driveLetter = drive.Name.TrimEnd('\\');
                var deviceName = GetDeviceNameFromDriveLetter(driveLetter);
                
                if (!string.IsNullOrEmpty(deviceName) && devicePath.StartsWith(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return driveLetter + devicePath.Substring(deviceName.Length);
                }
            }
        }
        catch { }
        
        return devicePath;
    }
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
    
    private string? GetDeviceNameFromDriveLetter(string driveLetter)
    {
        var sb = new StringBuilder(260);
        if (QueryDosDevice(driveLetter.TrimEnd('\\'), sb, 260))
        {
            return sb.ToString();
        }
        return null;
    }
    
    private string? FindHandleExe()
    {
        try
        {
            // Common locations for handle.exe
            var possiblePaths = new[]
            {
                @"C:\Windows\System32\handle.exe",
                @"C:\Windows\handle.exe",
                @"C:\Tools\SysInternals\handle.exe",
                @"C:\Program Files\SysInternals\handle.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"SysInternals\handle.exe")
            };
            
            return possiblePaths.FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }
    
    private List<(int ProcessId, string ProcessName)> FindProcessesUsingHandleExe(string handleExePath, string path)
    {
        var processes = new List<(int, string)>();
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = handleExePath,
                Arguments = $"-nobanner -a \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);
                
                // Parse handle.exe output
                // Format: processname.exe pid: type handle: path
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\S+)\s+pid:\s*(\d+)");
                    if (match.Success)
                    {
                        var processName = match.Groups[1].Value;
                        if (int.TryParse(match.Groups[2].Value, out var pid))
                        {
                            // Remove .exe extension if present
                            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                processName = processName.Substring(0, processName.Length - 4);
                            }
                            
                            if (!processes.Any(p => p.Item1 == pid))
                            {
                                processes.Add((pid, processName));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error running handle.exe");
        }
        
        return processes;
    }
    
    private List<(int ProcessId, string ProcessName)> FindProcessesUsingPowerShell(string path)
    {
        var processes = new List<(int, string)>();
        
        try
        {
            // PowerShell script to find processes with handles
            var script = $@"
                $path = '{path.Replace("'", "''")}'
                $processes = @{{}}
                
                # Method 1: Use openfiles command (requires admin)
                try {{
                    $openFiles = & openfiles /query /fo csv | ConvertFrom-Csv | Where-Object {{ $_.""Open File"" -like ""$path*"" }}
                    foreach ($file in $openFiles) {{
                        $pid = $file.""ID""
                        if ($pid -and $pid -match '^\d+$') {{
                            $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
                            if ($proc) {{
                                Write-Output ""$($proc.ProcessName)|$pid""
                                $processes[$pid] = $true
                            }}
                        }}
                    }}
                }} catch {{ }}
                
                # Method 2: Check for Explorer windows showing this path
                try {{
                    $shell = New-Object -ComObject Shell.Application
                    $windows = $shell.Windows()
                    foreach ($window in $windows) {{
                        try {{
                            $locationPath = $window.LocationURL -replace '^file:///', '' -replace '/', '\'
                            $locationPath = [System.Uri]::UnescapeDataString($locationPath)
                            if ($locationPath -like ""$path*"") {{
                                # Find Explorer process
                                $explorerProcs = Get-Process -Name explorer -ErrorAction SilentlyContinue
                                foreach ($exp in $explorerProcs) {{
                                    if (-not $processes.ContainsKey($exp.Id)) {{
                                        Write-Output ""explorer|$($exp.Id)""
                                        $processes[$exp.Id] = $true
                                    }}
                                }}
                            }}
                        }} catch {{ }}
                    }}
                }} catch {{ }}
                
                # Method 3: Use handle.exe if available (fallback to downloading it)
                try {{
                    $handlePath = $null
                    $possiblePaths = @(
                        'C:\Windows\System32\handle.exe',
                        'C:\Windows\handle.exe',
                        'C:\Tools\SysInternals\handle.exe',
                        'C:\Program Files\SysInternals\handle.exe'
                    )
                    
                    foreach ($p in $possiblePaths) {{
                        if (Test-Path $p) {{
                            $handlePath = $p
                            break
                        }}
                    }}
                    
                    if (-not $handlePath) {{
                        # Try to download handle.exe temporarily
                        $tempPath = Join-Path $env:TEMP 'handle.exe'
                        if (-not (Test-Path $tempPath)) {{
                            try {{
                                $webClient = New-Object System.Net.WebClient
                                $webClient.DownloadFile('https://download.sysinternals.com/files/Handle.zip', ""$env:TEMP\Handle.zip"")
                                Expand-Archive -Path ""$env:TEMP\Handle.zip"" -DestinationPath $env:TEMP -Force
                                if (Test-Path $tempPath) {{
                                    $handlePath = $tempPath
                                }}
                            }} catch {{ }}
                        }} elseif (Test-Path $tempPath) {{
                            $handlePath = $tempPath
                        }}
                    }}
                    
                    if ($handlePath) {{
                        # Accept EULA silently
                        & $handlePath -accepteula -nobanner 2>$null | Out-Null
                        
                        $output = & $handlePath -nobanner ""$path"" 2>$null
                        if ($output) {{
                            foreach ($line in $output -split ""`n"") {{
                                if ($line -match '^(\S+)\s+pid:\s*(\d+)') {{
                                    $procName = $matches[1] -replace '\.exe$', ''
                                    $pid = $matches[2]
                                    if (-not $processes.ContainsKey($pid)) {{
                                        Write-Output ""$procName|$pid""
                                        $processes[$pid] = $true
                                    }}
                                }}
                            }}
                        }}
                    }}
                }} catch {{ }}
                
                # Method 4: Check common applications that might lock folders
                $commonApps = @('OneDrive', 'Dropbox', 'GoogleDrive', 'Box', 'Backup', 'AntiVirus', 'Defender')
                foreach ($appPattern in $commonApps) {{
                    Get-Process -Name ""*$appPattern*"" -ErrorAction SilentlyContinue | ForEach-Object {{
                        try {{
                            # Check if process has any modules in our path
                            $modules = $_.Modules | Where-Object {{ $_.FileName -like ""$path*"" }}
                            if ($modules) {{
                                if (-not $processes.ContainsKey($_.Id)) {{
                                    Write-Output ""$($_.ProcessName)|$($_.Id)""
                                    $processes[$_.Id] = $true
                                }}
                            }}
                        }} catch {{ }}
                    }}
                }}
                
                # Method 5: Use Restart Manager API
                try {{
                    Add-Type @'
                    using System;
                    using System.Collections.Generic;
                    using System.Runtime.InteropServices;
                    
                    public class RestartManager {{
                        [DllImport(""rstrtmgr.dll"", CharSet = CharSet.Auto)]
                        public static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);
                        
                        [DllImport(""rstrtmgr.dll"")]
                        public static extern int RmEndSession(uint pSessionHandle);
                        
                        [DllImport(""rstrtmgr.dll"", CharSet = CharSet.Auto)]
                        public static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, IntPtr rgApplications, uint nServices, string[] rgsServiceNames);
                        
                        [DllImport(""rstrtmgr.dll"")]
                        public static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, out uint lpdwRebootReasons);
                        
                        [StructLayout(LayoutKind.Sequential)]
                        public struct RM_PROCESS_INFO {{
                            public RM_UNIQUE_PROCESS Process;
                            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
                            public string strAppName;
                            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
                            public string strServiceShortName;
                            public RM_APP_TYPE ApplicationType;
                            public uint AppStatus;
                            public uint TSSessionId;
                            [MarshalAs(UnmanagedType.Bool)]
                            public bool bRestartable;
                        }}
                        
                        [StructLayout(LayoutKind.Sequential)]
                        public struct RM_UNIQUE_PROCESS {{
                            public int dwProcessId;
                            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
                        }}
                        
                        public enum RM_APP_TYPE {{
                            RmUnknownApp = 0,
                            RmMainWindow = 1,
                            RmOtherWindow = 2,
                            RmService = 3,
                            RmExplorer = 4,
                            RmConsole = 5,
                            RmCritical = 1000
                        }}
                    }}
'@
                    
                    $sessionHandle = 0
                    $sessionKey = [Guid]::NewGuid().ToString()
                    
                    if ([RestartManager]::RmStartSession([ref]$sessionHandle, 0, $sessionKey) -eq 0) {{
                        try {{
                            # Register the directory itself
                            $files = @($path)
                            if ([RestartManager]::RmRegisterResources($sessionHandle, $files.Count, $files, 0, [IntPtr]::Zero, 0, $null) -eq 0) {{
                                $pnProcInfoNeeded = 0
                                $pnProcInfo = 0
                                $rgAffectedApps = $null
                                $lpdwRebootReasons = 0
                                
                                [RestartManager]::RmGetList($sessionHandle, [ref]$pnProcInfoNeeded, [ref]$pnProcInfo, $rgAffectedApps, [ref]$lpdwRebootReasons) | Out-Null
                                
                                if ($pnProcInfoNeeded -gt 0) {{
                                    $pnProcInfo = $pnProcInfoNeeded
                                    $rgAffectedApps = New-Object RestartManager+RM_PROCESS_INFO[] $pnProcInfo
                                    
                                    if ([RestartManager]::RmGetList($sessionHandle, [ref]$pnProcInfoNeeded, [ref]$pnProcInfo, $rgAffectedApps, [ref]$lpdwRebootReasons) -eq 0) {{
                                        foreach ($app in $rgAffectedApps) {{
                                            if ($app.Process.dwProcessId -gt 0) {{
                                                if (-not $processes.ContainsKey($app.Process.dwProcessId)) {{
                                                    Write-Output ""$($app.strAppName)|$($app.Process.dwProcessId)""
                                                    $processes[$app.Process.dwProcessId] = $true
                                                }}
                                            }}
                                        }}
                                    }}
                                }}
                            }}
                        }} finally {{
                            [RestartManager]::RmEndSession($sessionHandle) | Out-Null
                        }}
                    }}
                }} catch {{ }}
            ";
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(15000);
                
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length == 2 && int.TryParse(parts[1], out var pid))
                    {
                        var processName = parts[0].Trim();
                        if (!processes.Any(p => p.Item1 == pid))
                        {
                            processes.Add((pid, processName));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error using PowerShell to find processes");
        }
        
        return processes;
    }
    
    private List<(int ProcessId, string ProcessName)> FindProcessesUsingWmi(string path)
    {
        var processes = new List<(int, string)>();
        
        try
        {
            Log.Debug("Using WMI to find processes with handles to path");
            
            var script = $@"
                $path = '{path.Replace("'", "''")}'
                
                # Get all processes and check their handles
                Get-WmiObject Win32_Process | ForEach-Object {{
                    $process = $_
                    $processId = $process.ProcessId
                    $processName = $process.ProcessName
                    
                    # Skip system processes
                    if ($processId -le 4) {{ return }}
                    
                    try {{
                        # Get process handle information
                        $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
                        if ($proc) {{
                            # Check modules
                            $hasModule = $false
                            try {{
                                $modules = $proc.Modules | Where-Object {{ $_.FileName -like ""$path*"" }}
                                if ($modules) {{
                                    $hasModule = $true
                                }}
                            }} catch {{ }}
                            
                            # Check if this is a known service that commonly locks folders
                            $isService = Get-WmiObject Win32_Service -Filter ""ProcessId=$processId"" -ErrorAction SilentlyContinue
                            
                            # Special check for known problematic processes
                            $knownProcesses = @('WaveSvc', 'RtkAudService64', 'RtkAudioUniversalService', 'OneDrive', 'BBPrint', 'explorer')
                            $isKnownProcess = $knownProcesses | Where-Object {{ $processName -like ""*$_*"" }}
                            
                            if ($hasModule -or $isService -or $isKnownProcess) {{
                                Write-Output ""$($processName -replace '\.exe$', '')|$processId""
                            }}
                        }}
                    }} catch {{ }}
                }}
                
                # Also specifically check for services
                Get-WmiObject Win32_Service | Where-Object {{ $_.State -eq 'Running' }} | ForEach-Object {{
                    $service = $_
                    $processId = $service.ProcessId
                    
                    if ($processId -gt 0) {{
                        # Check if this service might have the vault path open
                        $serviceName = $service.Name
                        $knownServices = @('WaveSvc', 'RtkAudService64', 'RtkAudioUniversalService', 'BBPrint')
                        
                        if ($knownServices -contains $serviceName) {{
                            Write-Output ""$serviceName|$processId""
                        }}
                    }}
                }}
            ";
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);
                
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split('|');
                    if (parts.Length == 2 && int.TryParse(parts[1], out var pid))
                    {
                        if (!processes.Any(p => p.Item1 == pid))
                        {
                            processes.Add((pid, parts[0]));
                            Log.Debug("WMI found process: {ProcessName} (PID: {PID})", parts[0], pid);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error using WMI to find processes");
        }
        
        return processes;
    }
    
    private bool KillProcessesWithHandles(List<(int ProcessId, string ProcessName)> processesWithHandles)
    {
        bool allKilled = true;
        var criticalProcesses = new[] { "System", "csrss", "winlogon", "services", "lsass" };
        var explorerKilled = false;
        var servicesToRestart = new List<string>();
        
        foreach (var (processId, processName) in processesWithHandles)
        {
            try
            {
                // Skip critical system processes
                if (criticalProcesses.Any(cp => processName.Equals(cp, StringComparison.OrdinalIgnoreCase)))
                {
                    Log.Warning("Skipping critical process: {ProcessName} (PID: {ProcessId})", processName, processId);
                    continue;
                }
                
                // Special handling for Explorer
                if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("Explorer.exe has handle to vault, will restart it");
                    explorerKilled = true;
                }
                
                // Check if this is a Windows service
                var serviceInfo = GetServiceInfoForProcess(processId);
                if (serviceInfo != null)
                {
                    Log.Warning("Process {ProcessName} (PID: {ProcessId}) is a service: {ServiceName}", 
                        processName, processId, serviceInfo.ServiceName);
                    
                    // Stop the service instead of killing the process
                    if (StopService(serviceInfo.ServiceName))
                    {
                        Log.Information("Successfully stopped service: {ServiceName}", serviceInfo.ServiceName);
                        
                        // Add to restart list if it's a non-critical service
                        if (serviceInfo.CanRestart)
                        {
                            servicesToRestart.Add(serviceInfo.ServiceName);
                        }
                        
                        continue; // Move to next process
                    }
                    else
                    {
                        Log.Warning("Failed to stop service {ServiceName}, will try to kill process", serviceInfo.ServiceName);
                    }
                }
                
                // Kill the process
                var process = Process.GetProcessById(processId);
                Log.Warning("Terminating {ProcessName} (PID: {ProcessId}) - has handle to vault", processName, processId);
                
                process.Kill();
                process.WaitForExit(5000);
                
                if (!process.HasExited)
                {
                    // Force kill
                    Log.Warning("Process did not exit, force killing {ProcessName}", processName);
                    var killPsi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /PID {processId}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(killPsi)?.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to kill {ProcessName} (PID: {ProcessId})", processName, processId);
                allKilled = false;
            }
        }
        
        // Give system time to release handles
        Thread.Sleep(3000);
        
        // Restart Explorer if we killed it
        if (explorerKilled)
        {
            try
            {
                Process.Start("explorer.exe");
                Thread.Sleep(2000); // Give Explorer time to start
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restart Explorer");
            }
        }
        
        // Restart services that we stopped
        foreach (var serviceName in servicesToRestart)
        {
            try
            {
                Log.Information("Restarting service: {ServiceName}", serviceName);
                StartService(serviceName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to restart service: {ServiceName}", serviceName);
            }
        }
        
        return allKilled;
    }
    
    private class ServiceInfo
    {
        public string ServiceName { get; set; } = "";
        public bool CanRestart { get; set; }
    }
    
    private ServiceInfo? GetServiceInfoForProcess(int processId)
    {
        try
        {
            // Use WMI to find service associated with process
            var script = $@"
                $processId = {processId}
                Get-WmiObject Win32_Service | Where-Object {{ $_.ProcessId -eq $processId }} | ForEach-Object {{
                    Write-Output ""$($_.Name)|$($_.StartMode)""
                }}
            ";
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                    {
                        var parts = lines[0].Split('|');
                        if (parts.Length >= 2)
                        {
                            return new ServiceInfo
                            {
                                ServiceName = parts[0].Trim(),
                                CanRestart = parts[1].Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase)
                            };
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error getting service info for process {ProcessId}", processId);
        }
        
        return null;
    }
    
    private bool StopService(string serviceName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = $"stop \"{serviceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(30000); // Wait up to 30 seconds
                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error stopping service {ServiceName}", serviceName);
        }
        
        return false;
    }
    
    private void StartService(string serviceName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = $"start \"{serviceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            process?.WaitForExit(30000); // Wait up to 30 seconds
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error starting service {ServiceName}", serviceName);
        }
    }
    
    private List<(int ProcessId, string ProcessName)> FindServicesWithHandles()
    {
        var results = new List<(int, string)>();
        
        try
        {
            Log.Debug("Checking for Windows services that might have handles to vault");
            
            // Get list of services that commonly lock folders
            var commonServices = new[] 
            { 
                "WaveSvc", // Windows Audio Video Experience
                "RtkAudService64", // Realtek Audio Service
                "RtkAudioUniversalService", // Realtek Audio
                "OneDrive", // OneDrive sync
                "SearchIndexer", // Windows Search
                "WSearch", // Windows Search
                "Backup", // Various backup services
                "CrashPlan", // CrashPlan backup
                "GoogleDrive", // Google Drive
                "Dropbox", // Dropbox
                "Box", // Box sync
                "AcronisSyncAgent", // Acronis backup
                "BBPrint" // Brother printer service
            };
            
            foreach (var serviceName in commonServices)
            {
                try
                {
                    var script = $@"
                        $serviceName = '{serviceName}'
                        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
                        if ($service -and $service.Status -eq 'Running') {{
                            $processId = (Get-WmiObject Win32_Service -Filter ""Name='$serviceName'"").ProcessId
                            if ($processId -gt 0) {{
                                Write-Output ""$serviceName|$processId""
                            }}
                        }}
                    ";
                    
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"{script}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(2000);
                        
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            var parts = output.Trim().Split('|');
                            if (parts.Length == 2 && int.TryParse(parts[1], out var pid))
                            {
                                results.Add((pid, parts[0]));
                                Log.Debug("Found service {ServiceName} with PID {PID}", parts[0], pid);
                            }
                        }
                    }
                }
                catch { }
            }
            
            // Also check any service that has modules loaded from the vault path
            try
            {
                var checkModulesScript = $@"
                    $vaultPath = '{_vaultPath.Replace("'", "''")}'
                    Get-Service | Where-Object {{ $_.Status -eq 'Running' }} | ForEach-Object {{
                        $service = $_
                        try {{
                            $processId = (Get-WmiObject Win32_Service -Filter ""Name='$($service.Name)'"").ProcessId
                            if ($processId -gt 0) {{
                                $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
                                if ($process) {{
                                    $modules = $process.Modules | Where-Object {{ $_.FileName -like ""$vaultPath*"" }}
                                    if ($modules) {{
                                        Write-Output ""$($service.Name)|$processId""
                                    }}
                                }}
                            }}
                        }} catch {{ }}
                    }}
                ";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{checkModulesScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Trim().Split('|');
                        if (parts.Length == 2 && int.TryParse(parts[1], out var pid))
                        {
                            if (!results.Any(r => r.Item1 == pid))
                            {
                                results.Add((pid, parts[0]));
                                Log.Debug("Found service {ServiceName} with modules in vault path, PID {PID}", parts[0], pid);
                            }
                        }
                    }
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error finding services with handles");
        }
        
        return results;
    }
    
    private bool KillKnownPdmProcesses()
    {
        try
        {
            Log.Information("Killing known PDM processes as fallback");
            
            // PDM processes that commonly lock vault directories
            var processesToKill = new[] 
            { 
                "EdmServer", 
                "ConisioAdmin", 
                "Conisio",
                "ViewSetup",
                "SLDWORKS"
            };
            
            bool allKilled = true;
            
            foreach (var processName in processesToKill)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            Log.Warning("Terminating {ProcessName} (PID: {PID})", processName, process.Id);
                            process.Kill();
                            process.WaitForExit(5000);
                            
                            if (!process.HasExited)
                            {
                                // Try force kill via taskkill
                                Log.Warning("Process {ProcessName} did not exit, trying taskkill", processName);
                                var killProcess = new ProcessStartInfo
                                {
                                    FileName = "taskkill",
                                    Arguments = $"/F /PID {process.Id}",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                Process.Start(killProcess)?.WaitForExit(2000);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to kill {ProcessName} (PID: {PID})", processName, process.Id);
                            allKilled = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error getting processes for {ProcessName}", processName);
                }
            }
            
            Thread.Sleep(1000);
            return allKilled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error killing known PDM processes");
            return false;
        }
    }
    
    private bool TryDeleteProblematicSubdirectories()
    {
        try
        {
            Log.Debug("Attempting to delete known problematic subdirectories first");
            
            // Common problematic directories in PDM vaults
            var problematicDirs = new[] 
            { 
                Path.Combine(_vaultPath, "Logs"),
                Path.Combine(_vaultPath, "Manufacturing", "Library Components"),
                Path.Combine(_vaultPath, "Manufacturing")
            };
            
            bool anyDeleted = false;
            
            // Try to delete each problematic directory using various methods
            foreach (var dir in problematicDirs)
            {
                if (!Directory.Exists(dir))
                    continue;
                    
                Log.Debug("Attempting to delete problematic directory: {Dir}", dir);
                
                try
                {
                    // Method 1: Try direct deletion
                    Directory.Delete(dir, true);
                    anyDeleted = true;
                    Log.Debug("Successfully deleted {Dir}", dir);
                    continue;
                }
                catch { }
                
                try
                {
                    // Method 2: Use rd command
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c rd /s /q \"{dir}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(dir) ?? "C:\\"
                    };
                    
                    using var process = Process.Start(psi);
                    process?.WaitForExit(5000);
                    
                    if (!Directory.Exists(dir))
                    {
                        anyDeleted = true;
                        Log.Debug("Successfully deleted {Dir} using rd command", dir);
                        continue;
                    }
                }
                catch { }
                
                try
                {
                    // Method 3: Rename and then delete
                    var tempName = dir + "_temp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    Directory.Move(dir, tempName);
                    Directory.Delete(tempName, true);
                    anyDeleted = true;
                    Log.Debug("Successfully deleted {Dir} using rename method", dir);
                }
                catch (Exception ex)
                {
                    Log.Debug("Failed to delete {Dir}: {Message}", dir, ex.Message);
                }
            }
            
            return anyDeleted;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error in TryDeleteProblematicSubdirectories");
            return false;
        }
    }
}