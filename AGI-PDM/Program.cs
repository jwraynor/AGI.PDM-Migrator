using AGI_PDM.Configuration;
using AGI_PDM.Models;
using AGI_PDM.Services;
using AGI_PDM.Utils;
using Serilog;
using System.Text;

namespace AGI_PDM;

class Program
{
    private static MigrationConfig? _config;
    private static MigrationResult _result = new();

    static int Main(string[] _)
    {
        Console.OutputEncoding = Encoding.UTF8;
        
        try
        {
            // Display header and branding
            ConsoleUI.DisplayHeader();
            
            // Initialize configuration
            ConsoleUI.DisplayProgress("Loading configuration");
            var configManager = new ConfigManager();
            _config = configManager.LoadConfiguration();
            ConsoleUI.DisplayProgress("Loading configuration", true);

            // Initialize logger
            ConsoleUI.DisplayProgress("Initializing logging system");
            Logger.InitializeLogger(_config.Logging);
            ConsoleUI.DisplayProgress("Initializing logging system", true);

            Log.Information("AGI PDM Server Migration Tool started");
            Log.Information("Migrating from {OldServer} to {NewServer}", 
                _config.Migration.OldServer, _config.Migration.NewServer);
            
            // Check for PDM installation (friendly check, not an error)
            ConsoleUI.DisplaySection("System Requirements Check");
            ConsoleUI.DisplayProgress("Checking for SolidWorks PDM installation");
            
            var pdmInfo = PdmDetector.DetectPdmInstallation();
            ConsoleUI.DisplayPdmStatus(pdmInfo);
            
            if (!pdmInfo.IsInstalled)
            {
                Log.Information("SolidWorks PDM not detected - exiting gracefully");
                ConsoleUI.ShowExitMessage(0, "PDM installation check complete");
                return 0;  // Return 0 as this is not an error, just a requirement check
            }
            
            ConsoleUI.DisplayProgress("Checking for SolidWorks PDM installation", true);
            
            // If PDM detector found ViewSetup.exe, update the config
            if (!string.IsNullOrEmpty(pdmInfo.ViewSetupPath) && pdmInfo.ViewSetupPath != _config.Settings.ViewSetupPath)
            {
                Log.Information("Updating ViewSetup path from PDM detection: {Path}", pdmInfo.ViewSetupPath);
                _config.Settings.ViewSetupPath = pdmInfo.ViewSetupPath;
            }
            
            Console.WriteLine();
            
            // Display migration information
            ConsoleUI.DisplaySection("Migration Configuration");
            ConsoleUI.DisplayInfo("Source", _config.Migration.OldServer);
            ConsoleUI.DisplayInfo("Target", $"{_config.Migration.NewServer}:{_config.Migration.NewServerPort}");
            ConsoleUI.DisplayInfo("Vault", _config.Migration.VaultName);
            ConsoleUI.DisplayInfo("Path", _config.Migration.VaultPath);

            _result.StartTime = DateTime.Now;

            // Ensure admin privileges
            if (_config.Settings.RequireAdminRights)
            {
                AdminPrivileges.EnsureAdministratorPrivileges();
            }

            // Run the migration
            var success = RunMigration();

            _result.EndTime = DateTime.Now;
            _result.Success = success;

            // Report final status
            ReportFinalStatus();

            ConsoleUI.ShowExitMessage(success ? 0 : 1);
            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during migration");
            ConsoleUI.DisplayError($"Fatal error: {ex.Message}");
            ConsoleUI.ShowExitMessage(-1);
            return -1;
        }
        finally
        {
            Logger.CloseAndFlush();
        }
    }

    static bool RunMigration()
    {
        try
        {
            // Step 1: Pre-flight checks
            if (!RunPreflightChecks())
            {
                return false;
            }

            // Autonomous mode - proceed without confirmation
            ConsoleUI.DisplaySection("Starting Migration Process");
            ConsoleUI.DisplayInfo("Mode", "Autonomous - No user interaction required");
            Log.Information("Running in autonomous mode - proceeding without user confirmation");
            LogMigrationDetails();

            // Step 2: Update desktop.ini
            if (!UpdateDesktopIni())
            {
                return HandleStepFailure("Desktop.ini update");
            }

            // Step 3: Delete registry keys
            if (!DeleteRegistryKeys())
            {
                return HandleStepFailure("Registry key deletion");
            }

            // Step 4: Delete vault view
            if (!DeleteVaultView())
            {
                return HandleStepFailure("Vault view deletion");
            }

            // Step 5: Run View Setup
            if (!RunViewSetup())
            {
                return HandleStepFailure("View Setup");
            }

            Log.Information("Migration completed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during migration");
            _result.Errors.Add($"Unexpected error: {ex.Message}");
            return false;
        }
    }

    static bool RunPreflightChecks()
    {
        ConsoleUI.DisplaySection("Pre-flight Checks");
        ConsoleUI.DisplayProgress("Running system validation");
        
        _result.PreflightCheck.Start("Pre-flight Checks");
        Log.Information("Step 1: Running pre-flight checks...");

        try
        {
            var checker = new PreflightChecker(_config!);
            var success = checker.RunAllChecks();

            _result.Warnings.AddRange(checker.GetWarnings());
            _result.Errors.AddRange(checker.GetErrors());

            _result.PreflightCheck.Complete(success, 
                success ? null : "Pre-flight checks failed");

            ConsoleUI.DisplayProgress("Running system validation", true);
            if (success)
            {
                ConsoleUI.DisplaySuccess("All pre-flight checks passed");
            }
            else
            {
                ConsoleUI.DisplayError("Pre-flight checks failed - see log for details");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _result.PreflightCheck.Complete(false, ex.Message);
            Log.Error(ex, "Pre-flight checks failed");
            ConsoleUI.DisplayError($"Pre-flight checks error: {ex.Message}");
            return false;
        }
    }

    static void LogMigrationDetails()
    {
        Log.Information("===========================================");
        Log.Information("MIGRATION DETAILS");
        Log.Information("===========================================");
        Log.Information("Old Server: {OldServer}", _config!.Migration.OldServer);
        Log.Information("New Server: {NewServer}", _config.Migration.NewServer);
        Log.Information("Vault: {VaultName}", _config.Migration.VaultName);
        Log.Information("Path: {VaultPath}", _config.Migration.VaultPath);
        
        // Show current vault state
        if (Directory.Exists(_config.Migration.VaultPath))
        {
            var hasDesktopIni = File.Exists(Path.Combine(_config.Migration.VaultPath, "desktop.ini"));
            Log.Information("Vault Status: Directory exists (desktop.ini: {HasDesktopIni})", hasDesktopIni ? "present" : "missing");
        }
        else
        {
            Log.Information("Vault Status: Directory not found (may be already deleted)");
        }
        
        Log.Information("This process will:");
        Log.Information("- Modify desktop.ini file (if present)");
        Log.Information("- Delete registry entries for the old vault");
        Log.Information("- Delete the existing vault view");
        Log.Information("- Launch View Setup for new server connection");
        Log.Information("NOTE: The tool will skip steps that are not needed");
        Log.Information("WARNING: This operation cannot be easily undone!");
    }

    static bool UpdateDesktopIni()
    {
        ConsoleUI.DisplaySection("Step 1: Update Desktop.ini");
        ConsoleUI.DisplayProgress("Updating vault configuration");
        
        _result.DesktopIniUpdate.Start("Desktop.ini Update");
        Log.Information("Step 2: Updating desktop.ini file...");

        try
        {
            var manager = new DesktopIniManager(_config!.Migration.VaultPath, _config.Credentials.VaultOwnerOverride);
            var success = manager.UpdateAttachedByAttribute();

            if (manager.WasSkipped)
            {
                _result.DesktopIniUpdate.Skip(manager.SkipReason ?? "Step not required");
                ConsoleUI.DisplayWarning($"Skipped: {manager.SkipReason}");
            }
            else
            {
                _result.DesktopIniUpdate.Complete(success,
                    success ? null : "Failed to update desktop.ini");
                ConsoleUI.DisplayProgress("Updating vault configuration", true);
                if (success)
                {
                    ConsoleUI.DisplaySuccess("Desktop.ini updated successfully");
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _result.DesktopIniUpdate.Complete(false, ex.Message);
            Log.Error(ex, "Failed to update desktop.ini");
            return false;
        }
    }

    static bool DeleteRegistryKeys()
    {
        ConsoleUI.DisplaySection("Step 2: Clean Registry");
        ConsoleUI.DisplayProgress("Removing old vault registry entries");
        
        _result.RegistryDeletion.Start("Registry Key Deletion");
        Log.Information("Step 3: Deleting registry keys...");

        try
        {
            var manager = new RegistryManager(
                _config!.Migration.VaultName,
                _config.RegistryKeys.Primary,
                _config.RegistryKeys.Wow64,
                _config.Settings.BackupRegistry);

            var success = manager.DeleteVaultRegistryKeys();

            if (_config.Settings.BackupRegistry)
            {
                _result.BackupData = manager.GetBackupData();
            }

            _result.RegistryDeletion.Complete(success,
                success ? null : "Failed to delete registry keys");
            
            ConsoleUI.DisplayProgress("Removing old vault registry entries", true);
            if (success)
            {
                ConsoleUI.DisplaySuccess("Registry entries cleaned successfully");
            }
            else
            {
                ConsoleUI.DisplayError("Failed to clean registry entries");
            }

            return success;
        }
        catch (Exception ex)
        {
            _result.RegistryDeletion.Complete(false, ex.Message);
            Log.Error(ex, "Failed to delete registry keys");
            ConsoleUI.DisplayError($"Registry cleanup error: {ex.Message}");
            return false;
        }
    }

    static bool DeleteVaultView()
    {
        ConsoleUI.DisplaySection("Step 3: Delete Vault View");
        ConsoleUI.DisplayProgress("Removing existing vault view");
        
        _result.VaultViewDeletion.Start("Vault View Deletion");
        Log.Information("Step 4: Deleting vault view...");

        try
        {
            var manager = new VaultViewManager(
                _config!.Migration.VaultPath,
                _config.Migration.VaultName,
                _config.Migration.DeleteLocalCache);

            var success = manager.DeleteVaultView();

            if (success)
            {
                // Wait a bit to ensure deletion is complete
                success = manager.WaitForDeletion(30);
            }

            if (!success)
            {
                // Provide manual deletion instructions
                Log.Warning("Automated vault deletion failed. Manual deletion may be required.");
                ConsoleUI.DisplayWarning("Automated vault deletion failed");
                ConsoleUI.DisplayBox(
                    "MANUAL VAULT DELETION REQUIRED\n" +
                    "\n" +
                    "The automated deletion failed. Please try one of these methods:\n" +
                    "\n" +
                    "Method 1 (Recommended):\n" +
                    $"1. Open Windows Explorer to: {Path.GetDirectoryName(_config.Migration.VaultPath)}\n" +
                    $"2. Right-click on '{Path.GetFileName(_config.Migration.VaultPath)}' folder\n" +
                    "3. Select 'Delete File Vault View' from the context menu\n" +
                    "4. Check 'Delete the cached file vault files and folders'\n" +
                    "5. Click 'Delete'\n" +
                    "\n" +
                    "Method 2 (Alternative):\n" +
                    "1. Open ConisioAdmin.exe (PDM Administration tool)\n" +
                    "2. Navigate to the vault view settings\n" +
                    "3. Delete the vault view from there"
                );
                // In autonomous mode, we cannot wait for manual intervention
                Log.Error("Automated vault deletion failed. Manual intervention required.");
                Log.Error("The migration cannot continue in autonomous mode.");
                success = false;
            }
            else
            {
                ConsoleUI.DisplayProgress("Removing existing vault view", true);
                ConsoleUI.DisplaySuccess("Vault view deleted successfully");
            }

            _result.VaultViewDeletion.Complete(success,
                success ? null : "Failed to delete vault view");

            return success;
        }
        catch (Exception ex)
        {
            _result.VaultViewDeletion.Complete(false, ex.Message);
            Log.Error(ex, "Failed to delete vault view");
            ConsoleUI.DisplayError($"Vault deletion error: {ex.Message}");
            return false;
        }
    }

    static bool RunViewSetup()
    {
        ConsoleUI.DisplaySection("Step 4: Configure New Vault");
        ConsoleUI.DisplayProgress("Launching PDM View Setup");
        
        _result.ViewSetupExecution.Start("View Setup");
        Log.Information("Step 5: Running View Setup...");

        try
        {
            var automation = new ViewSetupAutomation(
                _config!.Settings.ViewSetupPath,
                _config.Migration.NewServer,
                _config.Migration.NewServerPort,
                _config.Credentials.PdmUser,
                _config.Credentials.PdmPassword,
                _config.Credentials.Domain);

            var success = automation.RunViewSetup();

            if (success)
            {
                Log.Information("View Setup launched. Waiting for completion...");
                ConsoleUI.DisplayProgress("Launching PDM View Setup", true);
                ConsoleUI.DisplaySuccess("View Setup launched successfully");
                ConsoleUI.DisplayInfo("Note", "View Setup is running in a separate window");
                success = automation.WaitForCompletion(5);
            }
            else
            {
                ConsoleUI.DisplayError("Failed to launch View Setup");
            }

            _result.ViewSetupExecution.Complete(success,
                success ? null : "View Setup did not complete successfully");

            return success;
        }
        catch (Exception ex)
        {
            _result.ViewSetupExecution.Complete(false, ex.Message);
            Log.Error(ex, "Failed to run View Setup");
            ConsoleUI.DisplayError($"View Setup error: {ex.Message}");
            return false;
        }
    }

    static bool HandleStepFailure(string stepName)
    {
        Log.Error("{StepName} failed", stepName);
        
        // In autonomous mode, we stop on failures
        Log.Error("Stopping migration due to step failure in autonomous mode");
        return false;
    }

    static void ReportFinalStatus()
    {
        ConsoleUI.DisplaySection("Migration Summary");
        
        Console.WriteLine($"Status:   {(_result.Success ? "[SUCCESS]" : "[FAILED]")}");
        Console.WriteLine($"Duration: {_result.Duration:mm\\:ss}");
        Console.WriteLine();

        // Report step statuses
        Console.WriteLine("Step Results:");
        Console.WriteLine(new string('-', 50));
        ReportStepStatus("Pre-flight Checks", _result.PreflightCheck);
        ReportStepStatus("Desktop.ini Update", _result.DesktopIniUpdate);
        ReportStepStatus("Registry Deletion", _result.RegistryDeletion);
        ReportStepStatus("Vault View Deletion", _result.VaultViewDeletion);
        ReportStepStatus("View Setup", _result.ViewSetupExecution);

        if (_result.Warnings.Any())
        {
            Console.WriteLine();
            ConsoleUI.DisplaySection("Warnings");
            foreach (var warning in _result.Warnings)
            {
                ConsoleUI.DisplayWarning(warning);
            }
        }

        if (_result.Errors.Any())
        {
            Console.WriteLine();
            ConsoleUI.DisplaySection("Errors");
            foreach (var error in _result.Errors)
            {
                ConsoleUI.DisplayError(error);
            }
        }

        if (_result.Success)
        {
            Console.WriteLine();
            ConsoleUI.DisplayBox(
                "NEXT STEPS:\\n" +
                "\\n" +
                "1. The View Setup window should be open\\n" +
                "2. Follow the on-screen instructions to complete the setup\\n" +
                "3. Users will authenticate with their own credentials\\n" +
                "\\n" +
                "The migration process has prepared your system for the new\\n" +
                "PDM server connection. Complete the View Setup to finish."
            );
        }

        // Autonomous mode - no user input
        Log.Information("Migration completed - exiting");
    }

    static void ReportStepStatus(string stepName, MigrationStepResult step)
    {
        string status;
        if (!step.Completed)
        {
            status = "[----] Not Run";
        }
        else if (step.Skipped)
        {
            status = "[SKIP] Skipped";
        }
        else if (step.Success)
        {
            status = "[ OK ] Success";
        }
        else
        {
            status = "[FAIL] Failed";
        }
        
        Console.WriteLine($"{stepName,-25} {status}");
        
        if (step.Skipped && !string.IsNullOrEmpty(step.SkipReason))
        {
            Console.WriteLine($"{"",25} Reason: {step.SkipReason}");
        }
        else if (!step.Success && !string.IsNullOrEmpty(step.ErrorMessage))
        {
            Console.WriteLine($"{"",25} Error: {step.ErrorMessage}");
        }
    }
}