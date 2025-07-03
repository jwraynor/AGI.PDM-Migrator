using Microsoft.Win32;
using Serilog;
using System.Security;

namespace AGI_PDM.Services;

public class RegistryManager
{
    private readonly string _vaultName;
    private readonly string _primaryKeyPath;
    private readonly string _wow64KeyPath;
    private readonly bool _backupEnabled;
    private readonly Dictionary<string, object?> _backupData = new();

    public RegistryManager(string vaultName, string primaryKeyPath, string wow64KeyPath, bool backupEnabled = true)
    {
        _vaultName = vaultName;
        _primaryKeyPath = primaryKeyPath;
        _wow64KeyPath = wow64KeyPath;
        _backupEnabled = backupEnabled;
    }

    public bool DeleteVaultRegistryKeys()
    {
        try
        {
            Log.Information("Starting registry key deletion for vault: {VaultName}", _vaultName);

            var success = true;

            // Delete primary registry key
            var primaryDeleted = DeleteRegistryKey(_primaryKeyPath, _vaultName, true);
            if (!primaryDeleted)
            {
                Log.Warning("Primary registry key not found or already deleted");
            }

            // Delete WOW64 registry key
            var wow64Deleted = DeleteRegistryKey(_wow64KeyPath, _vaultName, false);
            if (!wow64Deleted)
            {
                Log.Warning("WOW64 registry key not found or already deleted");
            }

            // If neither key was found, that's still a success - they're already gone
            success = true;

            if (success)
            {
                Log.Information("Successfully deleted registry keys for vault: {VaultName}", _vaultName);
            }
            else
            {
                Log.Error("Failed to delete any registry keys for vault: {VaultName}", _vaultName);
            }

            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during registry key deletion");
            return false;
        }
    }

    private bool DeleteRegistryKey(string keyPath, string vaultName, bool isPrimary)
    {
        try
        {
            // Parse the registry hive and subkey path
            var parts = keyPath.Split('\\', 2);
            if (parts.Length != 2)
            {
                Log.Error("Invalid registry key path format: {KeyPath}", keyPath);
                return false;
            }

            var hiveName = parts[0];
            var subKeyPath = parts[1];

            // Open the appropriate registry hive
            RegistryKey? hive = hiveName.ToUpperInvariant() switch
            {
                "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
                "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
                "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
                "HKEY_USERS" or "HKU" => Registry.Users,
                "HKEY_CURRENT_CONFIG" or "HKCC" => Registry.CurrentConfig,
                _ => null
            };

            if (hive == null)
            {
                Log.Error("Unknown registry hive: {HiveName}", hiveName);
                return false;
            }

            // Open the parent key
            using var parentKey = hive.OpenSubKey(subKeyPath, writable: true);
            if (parentKey == null)
            {
                Log.Debug("Parent registry key does not exist: {SubKeyPath}", subKeyPath);
                return false;
            }

            // Check if the vault key exists
            var vaultKeyExists = parentKey.GetSubKeyNames().Contains(vaultName, StringComparer.OrdinalIgnoreCase);
            if (!vaultKeyExists)
            {
                Log.Debug("Vault registry key does not exist: {VaultName} under {SubKeyPath}", vaultName, subKeyPath);
                return false;
            }

            // Backup the key if enabled
            if (_backupEnabled)
            {
                BackupRegistryKey(parentKey, vaultName, isPrimary);
            }

            // Delete the vault key
            Log.Debug("Deleting registry key: {VaultName} from {SubKeyPath}", vaultName, subKeyPath);
            parentKey.DeleteSubKeyTree(vaultName, throwOnMissingSubKey: false);

            Log.Information("Successfully deleted {KeyType} registry key for vault: {VaultName}", 
                isPrimary ? "primary" : "WOW64", vaultName);

            return true;
        }
        catch (SecurityException ex)
        {
            Log.Error(ex, "Security exception - insufficient permissions to delete registry key");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "Unauthorized access - cannot delete registry key");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error deleting registry key");
            return false;
        }
    }

    private void BackupRegistryKey(RegistryKey parentKey, string vaultName, bool isPrimary)
    {
        try
        {
            using var vaultKey = parentKey.OpenSubKey(vaultName);
            if (vaultKey == null) return;

            var backupKey = isPrimary ? "Primary" : "WOW64";
            var values = new Dictionary<string, object?>();

            // Backup all values
            foreach (var valueName in vaultKey.GetValueNames())
            {
                values[valueName] = vaultKey.GetValue(valueName);
            }

            _backupData[$"{backupKey}_{vaultName}"] = values;

            // Recursively backup subkeys
            foreach (var subKeyName in vaultKey.GetSubKeyNames())
            {
                using var subKey = vaultKey.OpenSubKey(subKeyName);
                if (subKey != null)
                {
                    BackupSubKey(subKey, $"{backupKey}_{vaultName}_{subKeyName}");
                }
            }

            Log.Debug("Backed up {KeyType} registry key for vault: {VaultName}", 
                isPrimary ? "primary" : "WOW64", vaultName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to backup registry key, continuing with deletion");
        }
    }

    private void BackupSubKey(RegistryKey key, string backupPath)
    {
        var values = new Dictionary<string, object?>();

        foreach (var valueName in key.GetValueNames())
        {
            values[valueName] = key.GetValue(valueName);
        }

        _backupData[backupPath] = values;

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            if (subKey != null)
            {
                BackupSubKey(subKey, $"{backupPath}_{subKeyName}");
            }
        }
    }

    public Dictionary<string, object?> GetBackupData()
    {
        return new Dictionary<string, object?>(_backupData);
    }

    public bool RestoreFromBackup()
    {
        if (!_backupData.Any())
        {
            Log.Warning("No backup data available to restore");
            return false;
        }
        try
        {
            Log.Information("Restoring registry keys from backup");
            // Implementation would go here if needed
            // This is a placeholder for potential rollback functionality
            Log.Warning("Registry restore not implemented - manual restoration may be required");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore registry keys from backup");
            return false;
        }
    }
}