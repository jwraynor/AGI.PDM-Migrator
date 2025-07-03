using System.Net.NetworkInformation;
using System.Security.Principal;
using Microsoft.Win32;
using Serilog;

namespace AGI_PDM.Services;

public class PreflightChecker
{
    private readonly Configuration.MigrationConfig _config;
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public PreflightChecker(Configuration.MigrationConfig config)
    {
        _config = config;
    }

    public bool RunAllChecks()
    {
        Log.Information("Running pre-flight checks...");

        _errors.Clear();
        _warnings.Clear();

        // Check admin privileges
        CheckAdminPrivileges();

        // Check PDM installation
        CheckPdmInstallation();

        // Check network connectivity
        CheckNetworkConnectivity();

        // Check vault directory
        CheckVaultDirectory();

        // Check for checked-out files
        if (_config.Settings.VerifyCheckedIn)
        {
            CheckForCheckedOutFiles();
        }

        // Check View Setup exists
        CheckViewSetupExists();

        // Report results
        ReportResults();

        return !_errors.Any();
    }

    private void CheckAdminPrivileges()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                _errors.Add("Application must be run with administrator privileges");
            }
            else
            {
                Log.Debug("Admin privileges confirmed");
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Failed to check admin privileges: {ex.Message}");
        }
    }

    private void CheckPdmInstallation()
    {
        try
        {
            // Check for PDM installation in registry
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise");
            
            if (key == null)
            {
                // Try WOW64 location
                using var wow64Key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Wow6432Node\SolidWorks\Applications\PDMWorks Enterprise");
                
                if (wow64Key == null)
                {
                    _errors.Add("SOLIDWORKS PDM does not appear to be installed");
                    return;
                }
            }

            // Check for PDM executable
            var pdmPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "SOLIDWORKS PDM"
            );

            if (!Directory.Exists(pdmPath))
            {
                pdmPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "SOLIDWORKS PDM"
                );

                if (!Directory.Exists(pdmPath))
                {
                    _warnings.Add("SOLIDWORKS PDM installation directory not found in expected location");
                }
            }

            Log.Debug("PDM installation confirmed");
        }
        catch (Exception ex)
        {
            _warnings.Add($"Could not verify PDM installation: {ex.Message}");
        }
    }

    private void CheckNetworkConnectivity()
    {
        try
        {
            // Check if old server is reachable (informational)
            if (!string.IsNullOrEmpty(_config.Migration.OldServer))
            {
                if (!IsServerReachable(_config.Migration.OldServer))
                {
                    _warnings.Add($"Cannot reach old server: {_config.Migration.OldServer} (this may be expected)");
                }
            }

            // Check if new server is reachable (critical)
            if (!string.IsNullOrEmpty(_config.Migration.NewServer))
            {
                if (!IsServerReachable(_config.Migration.NewServer))
                {
                    _errors.Add($"Cannot reach new PDM server: {_config.Migration.NewServer}");
                    _errors.Add("Please ensure you are connected to the AGI network (in-office or VPN)");
                }
                else
                {
                    Log.Debug("New PDM server is reachable");
                }
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Network connectivity check failed: {ex.Message}");
        }
    }

    private bool IsServerReachable(string serverName)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(serverName, 3000); // 3 second timeout
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            // Try DNS resolution as fallback
            try
            {
                var addresses = System.Net.Dns.GetHostAddresses(serverName);
                return addresses.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    private void CheckVaultDirectory()
    {
        try
        {
            if (!Directory.Exists(_config.Migration.VaultPath))
            {
                _warnings.Add($"Vault directory does not exist: {_config.Migration.VaultPath}");
                _warnings.Add("This may be normal if the vault view has already been deleted");
            }
            else
            {
                // Check if it's a valid vault directory
                var desktopIniPath = Path.Combine(_config.Migration.VaultPath, "desktop.ini");
                if (!File.Exists(desktopIniPath))
                {
                    // Check if this is an empty or partially deleted vault
                    var files = Directory.GetFiles(_config.Migration.VaultPath, "*", SearchOption.AllDirectories);
                    var isEmpty = files.Length == 0;
                    
                    if (isEmpty)
                    {
                        _warnings.Add($"Vault directory exists but is empty: {_config.Migration.VaultPath}");
                        _warnings.Add("This appears to be a partially deleted vault - deletion should complete successfully");
                    }
                    else
                    {
                        _warnings.Add("Vault directory exists but does not appear to be a PDM vault view (missing desktop.ini)");
                        _warnings.Add($"Directory contains {files.Length} file(s) - may be a partially migrated vault");
                        
                        // List a few files to help diagnose
                        var sampleFiles = files.Take(3).Select(f => Path.GetRelativePath(_config.Migration.VaultPath, f));
                        foreach (var file in sampleFiles)
                        {
                            _warnings.Add($"  - {file}");
                        }
                        if (files.Length > 3)
                        {
                            _warnings.Add($"  ... and {files.Length - 3} more files");
                        }
                    }
                }
                else
                {
                    Log.Debug("Vault directory exists and appears valid");
                }
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"Could not check vault directory: {ex.Message}");
        }
    }

    private void CheckForCheckedOutFiles()
    {
        try
        {
            if (!Directory.Exists(_config.Migration.VaultPath))
            {
                Log.Debug("Vault directory does not exist, skipping checked-out files check");
                return;
            }

            // Look for typical PDM lock files or checked-out indicators
            var checkedOutIndicators = new List<string>();

            // Walk through the vault directory looking for checked-out files
            // This is a simplified check - actual PDM API would be more accurate
            SearchForCheckedOutFiles(_config.Migration.VaultPath, checkedOutIndicators);

            if (checkedOutIndicators.Any())
            {
                _errors.Add("Found potential checked-out files in the vault:");
                foreach (var file in checkedOutIndicators.Take(10)) // Show first 10
                {
                    _errors.Add($"  - {file}");
                }
                if (checkedOutIndicators.Count > 10)
                {
                    _errors.Add($"  ... and {checkedOutIndicators.Count - 10} more files");
                }
                _errors.Add("Please ensure all files are checked in before proceeding");
            }
            else
            {
                Log.Debug("No checked-out files detected");
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"Could not check for checked-out files: {ex.Message}");
        }
    }

    private void SearchForCheckedOutFiles(string directory, List<string> checkedOutFiles)
    {
        try
        {
            // Look for lock files or other PDM indicators
            var files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);
            
            foreach (var file in files)
            {
                // PDM often uses specific file attributes or lock files
                var fileInfo = new FileInfo(file);
                
                // Check for common PDM lock file patterns
                if (file.EndsWith(".~vf", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains("~$") ||
                    (fileInfo.Attributes & FileAttributes.ReadOnly) != FileAttributes.ReadOnly)
                {
                    // In PDM, checked-in files are typically read-only
                    // This is a simplified check
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension == ".sldprt" || extension == ".sldasm" || extension == ".slddrw")
                    {
                        checkedOutFiles.Add(Path.GetRelativePath(_config.Migration.VaultPath, file));
                    }
                }
            }

            // Recursively check subdirectories
            var subdirectories = Directory.GetDirectories(directory);
            foreach (var subdirectory in subdirectories)
            {
                SearchForCheckedOutFiles(subdirectory, checkedOutFiles);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error searching directory: {Directory}", directory);
        }
    }

    private void CheckViewSetupExists()
    {
        try
        {
            if (!File.Exists(_config.Settings.ViewSetupPath))
            {
                _errors.Add($"View Setup executable not found at: {_config.Settings.ViewSetupPath}");
                
                // Try to find it in common locations
                var alternativePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
                        "SOLIDWORKS PDM", "ViewSetup.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 
                        "SOLIDWORKS PDM", "ViewSetup.exe")
                };

                foreach (var path in alternativePaths)
                {
                    if (File.Exists(path))
                    {
                        _warnings.Add($"View Setup found at alternative location: {path}");
                        _warnings.Add("Consider updating config.json with the correct path");
                        break;
                    }
                }
            }
            else
            {
                Log.Debug("View Setup executable found");
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Could not check for View Setup: {ex.Message}");
        }
    }

    private void ReportResults()
    {
        if (_warnings.Any())
        {
            Log.Warning("Pre-flight check warnings:");
            foreach (var warning in _warnings)
            {
                Log.Warning("  - {Warning}", warning);
            }
        }

        if (_errors.Any())
        {
            Log.Error("Pre-flight check errors:");
            foreach (var error in _errors)
            {
                Log.Error("  - {Error}", error);
            }
            Log.Error("Pre-flight checks failed. Please resolve the above issues before continuing.");
        }
        else
        {
            Log.Information("All pre-flight checks passed successfully");
        }
    }

    public List<string> GetErrors() => new(_errors);
    public List<string> GetWarnings() => new(_warnings);
}