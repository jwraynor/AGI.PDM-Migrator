using System.Diagnostics;
using System.Security.Principal;
using System.Management;
using Serilog;

namespace AGI_PDM.Services;

public class DesktopIniManager
{
    private readonly string _vaultPath;
    private readonly string _desktopIniPath;
    private readonly string? _userOverride;
    
    public bool WasSkipped { get; private set; }
    public string? SkipReason { get; private set; }

    public DesktopIniManager(string vaultPath, string? userOverride = null)
    {
        _vaultPath = vaultPath;
        _desktopIniPath = Path.Combine(_vaultPath, "desktop.ini");
        _userOverride = userOverride;
    }

    public bool UpdateAttachedByAttribute()
    {
        try
        {
            Log.Information("Starting desktop.ini modification for vault at {VaultPath}", _vaultPath);

            if (!Directory.Exists(_vaultPath))
            {
                Log.Error("Vault directory does not exist: {VaultPath}", _vaultPath);
                return false;
            }

            if (!File.Exists(_desktopIniPath))
            {
                Log.Warning("desktop.ini file not found at: {DesktopIniPath}", _desktopIniPath);
                Log.Information("This may indicate the vault was already partially deleted or modified");
                
                // Check if this is a partially deleted vault
                var files = Directory.GetFiles(_vaultPath, "*", SearchOption.TopDirectoryOnly);
                var subdirs = Directory.GetDirectories(_vaultPath, "*", SearchOption.TopDirectoryOnly);
                
                if (files.Length == 0 && subdirs.Length == 0)
                {
                    Log.Information("Vault directory is empty - skipping desktop.ini update");
                    WasSkipped = true;
                    SkipReason = "Vault directory is empty";
                    return true; // Not an error, vault is essentially gone
                }
                
                Log.Warning("desktop.ini missing but vault contains {FileCount} files and {DirCount} directories", 
                    files.Length, subdirs.Length);
                Log.Information("Skipping desktop.ini update - this step is not critical for migration");
                
                WasSkipped = true;
                SkipReason = "desktop.ini file not found (non-critical)";
                
                // Return true to allow migration to continue
                // The desktop.ini update is helpful but not essential for the migration
                return true;
            }

            // Get current user name
            var currentUser = GetCurrentUserName();
            
            // Use override if provided
            if (!string.IsNullOrWhiteSpace(_userOverride))
            {
                Log.Information("Using configured user override: {UserOverride}", _userOverride);
                currentUser = _userOverride;
            }
            
            if (string.IsNullOrEmpty(currentUser))
            {
                Log.Error("Failed to get current user name");
                return false;
            }

            Log.Information("Current user: {CurrentUser}", currentUser);

            // Remove file attributes
            if (!RemoveFileAttributes())
            {
                return false;
            }

            // Update the desktop.ini file
            if (!UpdateDesktopIni(currentUser))
            {
                RestoreFileAttributes(); // Try to restore attributes even if update fails
                return false;
            }

            // Restore file attributes
            if (!RestoreFileAttributes())
            {
                Log.Warning("Failed to restore file attributes, but update was successful");
            }

            Log.Information("Successfully updated desktop.ini with AttachedBy={CurrentUser}", currentUser);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating desktop.ini file");
            return false;
        }
    }

    private string GetCurrentUserName()
    {
        try
        {
            // When running as admin, we need to get the actual logged-in user, not the admin account
            
            // Method 1: Try to get the user who launched the process (before elevation)
            var actualUser = GetActualLoggedInUser();
            if (!string.IsNullOrEmpty(actualUser))
            {
                Log.Information("Found actual logged-in user: {User}", actualUser);
                return actualUser;
            }

            // Method 2: Use WHOAMI command as specified in the document
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c whoami",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                return output;
            }

            // Method 3: Fallback to WindowsIdentity
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get current user name");
            return string.Empty;
        }
    }

    private string GetActualLoggedInUser()
    {
        try
        {
            // Method 1: Check active console session user
            var sessionUser = GetConsoleSessionUser();
            if (!string.IsNullOrEmpty(sessionUser) && !sessionUser.Contains("su-", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("Found console session user: {User}", sessionUser);
                return sessionUser;
            }

            // Method 2: Try to get the user from the Explorer process
            var explorerProcesses = Process.GetProcessesByName("explorer");
            if (explorerProcesses.Length > 0)
            {
                foreach (var explorerProcess in explorerProcesses)
                {
                    try
                    {
                        // Use WMI to get the process owner
                        var query = $"SELECT * FROM Win32_Process WHERE ProcessId = {explorerProcess.Id}";
                        using var searcher = new ManagementObjectSearcher(query);
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            var argList = new string[] { string.Empty, string.Empty };
                            var returnVal = Convert.ToInt32(obj.InvokeMethod("GetOwner", argList));
                            if (returnVal == 0)
                            {
                                var owner = $"{argList[1]}\\{argList[0]}"; // DOMAIN\username
                                
                                // Skip if this is an admin/service account
                                if (!owner.Contains("su-", StringComparison.OrdinalIgnoreCase) &&
                                    !owner.Contains("admin", StringComparison.OrdinalIgnoreCase))
                                {
                                    Log.Debug("Found Explorer process owner: {User}", owner);
                                    return owner;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // Method 3: Check who owns the user's profile directory
            var usersDir = @"C:\Users";
            var userDirs = Directory.GetDirectories(usersDir)
                .Where(d => !d.EndsWith("Public") && 
                           !d.EndsWith("Default") && 
                           !d.EndsWith("All Users") &&
                           !d.Contains("su-", StringComparison.OrdinalIgnoreCase) &&
                           !d.Contains("admin", StringComparison.OrdinalIgnoreCase))
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.LastAccessTime)
                .FirstOrDefault();

            if (userDirs != null)
            {
                var userName = userDirs.Name;
                // Try to get the full domain\username format
                var domainName = Environment.UserDomainName ?? Environment.MachineName;
                var fullName = $"{domainName}\\{userName}";
                Log.Debug("Found user from profile directory: {User}", fullName);
                return fullName;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not determine actual logged-in user");
            return string.Empty;
        }
    }

    private string GetConsoleSessionUser()
    {
        try
        {
            // Use WMI to get the active console session
            using var searcher = new ManagementObjectSearcher("SELECT UserName, Domain FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var userName = obj["UserName"]?.ToString();
                var domain = obj["Domain"]?.ToString();
                
                if (!string.IsNullOrEmpty(userName))
                {
                    // Format as DOMAIN\username
                    if (!string.IsNullOrEmpty(domain) && !userName.Contains("\\"))
                    {
                        return $"{domain}\\{userName}";
                    }
                    return userName;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to get console session user via WMI");
        }
        
        return string.Empty;
    }

    private bool RemoveFileAttributes()
    {
        try
        {
            Log.Debug("Removing file attributes from desktop.ini");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "attrib",
                    Arguments = $"-s -h -r \"{_desktopIniPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _vaultPath
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Log.Error("Failed to remove file attributes. Exit code: {ExitCode}", process.ExitCode);
                return false;
            }

            Log.Debug("Successfully removed file attributes");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing file attributes");
            return false;
        }
    }

    private bool RestoreFileAttributes()
    {
        try
        {
            Log.Debug("Restoring file attributes on desktop.ini");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "attrib",
                    Arguments = $"+s +h +r \"{_desktopIniPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _vaultPath
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Log.Error("Failed to restore file attributes. Exit code: {ExitCode}", process.ExitCode);
                return false;
            }

            Log.Debug("Successfully restored file attributes");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error restoring file attributes");
            return false;
        }
    }

    private bool UpdateDesktopIni(string currentUser)
    {
        try
        {
            Log.Debug("Reading desktop.ini file");

            var lines = File.ReadAllLines(_desktopIniPath);
            var updated = false;
            var newLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("AttachedBy=", StringComparison.OrdinalIgnoreCase))
                {
                    newLines.Add($"AttachedBy={currentUser}");
                    updated = true;
                    Log.Debug("Updated existing AttachedBy entry");
                }
                else
                {
                    newLines.Add(line);
                }
            }

            // If AttachedBy wasn't found, add it
            if (!updated)
            {
                // Find the section that contains vault settings (usually after [.ShellClassInfo])
                for (int i = 0; i < newLines.Count; i++)
                {
                    if (newLines[i].Contains("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
                    {
                        // Insert after the section header
                        newLines.Insert(i + 1, $"AttachedBy={currentUser}");
                        updated = true;
                        Log.Debug("Added new AttachedBy entry");
                        break;
                    }
                }
            }

            if (!updated)
            {
                Log.Warning("Could not find appropriate location for AttachedBy entry, appending to end");
                newLines.Add($"AttachedBy={currentUser}");
            }

            Log.Debug("Writing updated desktop.ini file");
            File.WriteAllLines(_desktopIniPath, newLines);

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating desktop.ini content");
            return false;
        }
    }
}