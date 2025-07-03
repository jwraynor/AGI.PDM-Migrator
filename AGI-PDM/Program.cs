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

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        
        try
        {
            // Initialize configuration
            var configManager = new ConfigManager();
            _config = configManager.LoadConfiguration();

            // Initialize logger
            Logger.InitializeLogger(_config.Logging);

            Log.Information("===========================================");
            Log.Information("AGI PDM Server Migration Tool");
            Log.Information("===========================================");
            Log.Information("Migrating from {OldServer} to {NewServer}", 
                _config.Migration.OldServer, _config.Migration.NewServer);

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

            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during migration");
            Console.WriteLine($"\nFATAL ERROR: {ex.Message}");
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
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

            // Confirm before proceeding with destructive operations
            if (!GetUserConfirmation())
            {
                Log.Information("Migration cancelled by user");
                return false;
            }

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

            return success;
        }
        catch (Exception ex)
        {
            _result.PreflightCheck.Complete(false, ex.Message);
            Log.Error(ex, "Pre-flight checks failed");
            return false;
        }
    }

    static bool GetUserConfirmation()
    {
        Console.WriteLine("\n===========================================");
        Console.WriteLine("MIGRATION CONFIRMATION");
        Console.WriteLine("===========================================");
        Console.WriteLine($"Old Server: {_config!.Migration.OldServer}");
        Console.WriteLine($"New Server: {_config.Migration.NewServer}");
        Console.WriteLine($"Vault: {_config.Migration.VaultName}");
        Console.WriteLine($"Path: {_config.Migration.VaultPath}");
        
        // Show current vault state
        if (Directory.Exists(_config.Migration.VaultPath))
        {
            var hasDesktopIni = File.Exists(Path.Combine(_config.Migration.VaultPath, "desktop.ini"));
            Console.WriteLine($"\nVault Status: Directory exists (desktop.ini: {(hasDesktopIni ? "present" : "missing")})");
        }
        else
        {
            Console.WriteLine("\nVault Status: Directory not found (may be already deleted)");
        }
        
        Console.WriteLine("\nThis process will:");
        Console.WriteLine("- Modify desktop.ini file (if present)");
        Console.WriteLine("- Delete registry entries for the old vault");
        Console.WriteLine("- Delete the existing vault view");
        Console.WriteLine("- Launch View Setup for new server connection");
        Console.WriteLine("\nNOTE: The tool will skip steps that are not needed");
        Console.WriteLine("WARNING: This operation cannot be easily undone!");
        Console.WriteLine("\nDo you want to continue? (Y/N): ");

        var response = Console.ReadKey();
        Console.WriteLine();

        return response.Key == ConsoleKey.Y;
    }

    static bool UpdateDesktopIni()
    {
        _result.DesktopIniUpdate.Start("Desktop.ini Update");
        Log.Information("Step 2: Updating desktop.ini file...");

        try
        {
            var manager = new DesktopIniManager(_config!.Migration.VaultPath, _config.Credentials.VaultOwnerOverride);
            var success = manager.UpdateAttachedByAttribute();

            if (manager.WasSkipped)
            {
                _result.DesktopIniUpdate.Skip(manager.SkipReason ?? "Step not required");
            }
            else
            {
                _result.DesktopIniUpdate.Complete(success,
                    success ? null : "Failed to update desktop.ini");
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

            return success;
        }
        catch (Exception ex)
        {
            _result.RegistryDeletion.Complete(false, ex.Message);
            Log.Error(ex, "Failed to delete registry keys");
            return false;
        }
    }

    static bool DeleteVaultView()
    {
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
                Console.WriteLine("\n===========================================");
                Console.WriteLine("MANUAL VAULT DELETION REQUIRED");
                Console.WriteLine("===========================================");
                Console.WriteLine("The automated deletion failed. Please try one of these methods:");
                Console.WriteLine();
                Console.WriteLine("Method 1 (Recommended):");
                Console.WriteLine($"1. Open Windows Explorer and navigate to: {Path.GetDirectoryName(_config.Migration.VaultPath)}");
                Console.WriteLine($"2. Right-click on '{Path.GetFileName(_config.Migration.VaultPath)}' folder");
                Console.WriteLine("3. Select 'Delete File Vault View' from the context menu");
                Console.WriteLine("4. Check 'Delete the cached file vault files and folders from the local hard disk'");
                Console.WriteLine("5. Click 'Delete'");
                Console.WriteLine();
                Console.WriteLine("Method 2 (Alternative):");
                Console.WriteLine("1. Open ConisioAdmin.exe (PDM Administration tool)");
                Console.WriteLine("2. Navigate to the vault view settings");
                Console.WriteLine("3. Delete the vault view from there");
                Console.WriteLine();
                Console.WriteLine("After manual deletion, continue? (Y/N): ");
                
                var response = Console.ReadKey();
                Console.WriteLine();
                
                if (response.Key == ConsoleKey.Y)
                {
                    // Check if it was manually deleted
                    if (!Directory.Exists(_config.Migration.VaultPath))
                    {
                        Log.Information("Vault manually deleted successfully");
                        success = true;
                    }
                    else
                    {
                        Log.Warning("Vault still exists, continuing anyway");
                        success = true; // Allow continuation
                    }
                }
            }

            _result.VaultViewDeletion.Complete(success,
                success ? null : "Failed to delete vault view");

            return success;
        }
        catch (Exception ex)
        {
            _result.VaultViewDeletion.Complete(false, ex.Message);
            Log.Error(ex, "Failed to delete vault view");
            return false;
        }
    }

    static bool RunViewSetup()
    {
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
                success = automation.WaitForCompletion(5);
            }

            _result.ViewSetupExecution.Complete(success,
                success ? null : "View Setup did not complete successfully");

            return success;
        }
        catch (Exception ex)
        {
            _result.ViewSetupExecution.Complete(false, ex.Message);
            Log.Error(ex, "Failed to run View Setup");
            return false;
        }
    }

    static bool HandleStepFailure(string stepName)
    {
        Log.Error("{StepName} failed", stepName);
        
        Console.WriteLine($"\n{stepName} failed. Do you want to continue anyway? (Y/N): ");
        var response = Console.ReadKey();
        Console.WriteLine();

        if (response.Key == ConsoleKey.Y)
        {
            Log.Warning("User chose to continue despite {StepName} failure", stepName);
            return true;
        }

        return false;
    }

    static void ReportFinalStatus()
    {
        Console.WriteLine("\n===========================================");
        Console.WriteLine("MIGRATION SUMMARY");
        Console.WriteLine("===========================================");
        Console.WriteLine($"Status: {(_result.Success ? "SUCCESS" : "FAILED")}");
        Console.WriteLine($"Duration: {_result.Duration:mm\\:ss}");
        Console.WriteLine();

        // Report step statuses
        ReportStepStatus("Pre-flight Checks", _result.PreflightCheck);
        ReportStepStatus("Desktop.ini Update", _result.DesktopIniUpdate);
        ReportStepStatus("Registry Deletion", _result.RegistryDeletion);
        ReportStepStatus("Vault View Deletion", _result.VaultViewDeletion);
        ReportStepStatus("View Setup", _result.ViewSetupExecution);

        if (_result.Warnings.Any())
        {
            Console.WriteLine("\nWarnings:");
            foreach (var warning in _result.Warnings)
            {
                Console.WriteLine($"  - {warning}");
            }
        }

        if (_result.Errors.Any())
        {
            Console.WriteLine("\nErrors:");
            foreach (var error in _result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        if (_result.Success)
        {
            Console.WriteLine("\nNext Steps:");
            Console.WriteLine("1. The View Setup window should be open");
            Console.WriteLine("2. Follow the on-screen instructions to complete the setup");
            Console.WriteLine("3. Users will authenticate with their own credentials");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static void ReportStepStatus(string stepName, MigrationStepResult step)
    {
        string status;
        if (!step.Completed)
        {
            status = "- Not Run";
        }
        else if (step.Skipped)
        {
            status = "→ Skipped";
        }
        else if (step.Success)
        {
            status = "✓ Success";
        }
        else
        {
            status = "✗ Failed";
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