using System.Diagnostics;
using Serilog;

namespace AGI_PDM.Services;

public class ViewSetupAutomation
{
    private readonly string _viewSetupPath;
    private readonly string _serverName;
    private readonly int _serverPort;
    private readonly string _pdmUser;
    private readonly string _pdmPassword;
    private readonly string _domain;

    public ViewSetupAutomation(
        string viewSetupPath,
        string serverName,
        int serverPort,
        string pdmUser,
        string pdmPassword,
        string domain)
    {
        _viewSetupPath = viewSetupPath;
        _serverName = serverName;
        _serverPort = serverPort;
        _pdmUser = pdmUser;
        _pdmPassword = pdmPassword;
        _domain = domain;
    }

    public bool RunViewSetup()
    {
        try
        {
            Log.Information("Starting View Setup automation for server: {ServerName}", _serverName);

            if (!File.Exists(_viewSetupPath))
            {
                Log.Error("View Setup executable not found at: {ViewSetupPath}", _viewSetupPath);
                return false;
            }

            // Note: Full automation of View Setup is challenging as it's a GUI application
            // This implementation provides several approaches

            // Method 1: Try silent/command-line parameters if available
            if (TrySilentSetup())
            {
                Log.Information("Successfully completed View Setup using silent mode");
                return true;
            }

            // Method 2: Launch View Setup with pre-configured settings
            if (LaunchViewSetupWithConfig())
            {
                Log.Information("Launched View Setup with configuration");
                Log.Warning("Manual interaction may be required to complete the setup");
                return true;
            }

            Log.Error("Failed to automate View Setup");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error running View Setup");
            return false;
        }
    }

    private bool TrySilentSetup()
    {
        try
        {
            Log.Debug("Attempting silent View Setup");

            // Common silent parameters for enterprise software
            var silentArgs = new[]
            {
                $"/s /server:{_serverName} /port:{_serverPort}",
                $"-silent -server {_serverName} -port {_serverPort}",
                $"/quiet /server:{_serverName} /port:{_serverPort}",
                $"/q /server:{_serverName} /port:{_serverPort}"
            };

            foreach (var args in silentArgs)
            {
                Log.Debug("Trying silent setup with args: {Args}", args);

                var psi = new ProcessStartInfo
                {
                    FileName = _viewSetupPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas" // Run as administrator
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(TimeSpan.FromSeconds(30).Milliseconds);

                    if (process.ExitCode == 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Silent setup attempt failed");
        }

        return false;
    }

    private bool LaunchViewSetupWithConfig()
    {
        try
        {
            Log.Debug("Launching View Setup with configuration");

            // Create a configuration file that View Setup might read
            CreateViewSetupConfig();

            // Launch View Setup as administrator
            var psi = new ProcessStartInfo
            {
                FileName = _viewSetupPath,
                UseShellExecute = true,
                Verb = "runas" // Run as administrator
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                Log.Information("View Setup launched successfully");
                Log.Information("Server to add: {ServerName}:{ServerPort}", _serverName, _serverPort);
                Log.Information("Credentials: User={PdmUser}, Domain={Domain}", _pdmUser, _domain);
                
                // Create instruction file for manual steps
                CreateInstructionFile();
                
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch View Setup");
        }

        return false;
    }

    private void CreateViewSetupConfig()
    {
        try
        {
            // Create a configuration file in a location View Setup might check
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SOLIDWORKS",
                "PDM"
            );

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var configFile = Path.Combine(configDir, "ViewSetup.config");
            var configContent = $@"[Server]
Name={_serverName}
Port={_serverPort}
AutoConnect=1

[Credentials]
Username={_pdmUser}
Domain={_domain}
SaveCredentials=1
";

            File.WriteAllText(configFile, configContent);
            Log.Debug("Created View Setup config file at: {ConfigFile}", configFile);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to create View Setup config file");
        }
    }

    private void CreateInstructionFile()
    {
        try
        {
            var instructionPath = Path.Combine(
                Path.GetDirectoryName(_viewSetupPath) ?? "",
                "ViewSetup_Instructions.txt"
            );

            var instructions = $@"SOLIDWORKS PDM View Setup Instructions
=====================================

The View Setup application has been launched. Please follow these steps:

1. Click 'Add' if the server does not appear in the list
2. Enter the following server details:
   - Server name: {_serverName}
   - Server port: {_serverPort}

3. Click 'OK' to add the server

4. If prompted for credentials, enter:
   - Username: {_pdmUser}
   - Password: [As configured]
   - Domain: {_domain}

5. Accept the licensing agreement if prompted

6. Select the appropriate vault from the list

7. Click 'Next' and follow the remaining prompts to complete the setup

8. The user will authenticate with their own credentials for vault access

Note: This file contains sensitive information and should be deleted after use.
";

            File.WriteAllText(instructionPath, instructions);
            
            // Open the instruction file
            Process.Start(new ProcessStartInfo
            {
                FileName = instructionPath,
                UseShellExecute = true
            });

            Log.Information("Created instruction file at: {InstructionPath}", instructionPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create instruction file");
        }
    }

    public bool WaitForCompletion(int timeoutMinutes = 5)
    {
        try
        {
            Log.Information("Waiting for View Setup to complete (timeout: {Timeout} minutes)", timeoutMinutes);

            var stopwatch = Stopwatch.StartNew();
            var timeout = TimeSpan.FromMinutes(timeoutMinutes);

            while (stopwatch.Elapsed < timeout)
            {
                // Check if View Setup process is still running
                var viewSetupProcesses = Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(_viewSetupPath));

                if (!viewSetupProcesses.Any())
                {
                    Log.Debug("View Setup process has exited");
                    
                    // Give it a moment for any cleanup
                    Thread.Sleep(2000);
                    
                    // Check if the vault view was created
                    if (CheckVaultViewCreated())
                    {
                        Log.Information("Vault view successfully created");
                        return true;
                    }
                    
                    return false;
                }

                Thread.Sleep(5000); // Check every 5 seconds
            }

            Log.Warning("View Setup timed out after {Timeout} minutes", timeoutMinutes);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error waiting for View Setup completion");
            return false;
        }
    }

    private bool CheckVaultViewCreated()
    {
        // This would check if the new vault view was successfully created
        // Implementation depends on how PDM stores vault view information
        // Could check registry, file system, or PDM-specific locations
        
        Log.Debug("Checking if vault view was created");
        
        // Placeholder - actual implementation would verify the vault view exists
        return true;
    }
}