using System.Diagnostics;
using System.Runtime.InteropServices;
using EPDM.Interop.epdm;
using Serilog;

namespace AGI_PDM.Services;

/// <summary>
/// Service for interacting with SolidWorks PDM API to properly delete vault views
/// </summary>
public class PdmVaultService
{

    private readonly string _vaultName;
    private readonly string _vaultPath;

    public PdmVaultService(string vaultName, string vaultPath)
    {
        _vaultName = vaultName;
        _vaultPath = vaultPath;
    }

    /// <summary>
    /// Deletes the vault view using the PDM API
    /// </summary>
    public bool DeleteVaultViewUsingApi()
    {
        // Check if PDM is installed
        if (!IsPdmInstalled())
        {
            Log.Warning("PDM API not available, using alternative methods");
            return TryDeleteVaultViewAlternative();
        }
        
        IEdmVault5? vault = null;
        
        try
        {
            Log.Information("Attempting to delete vault view using PDM API");
            
            // Create vault object
            vault = new EdmVault5() as IEdmVault5;
            if (vault == null)
            {
                Log.Error("Failed to create PDM vault object");
                return false;
            }

            try
            {
                // Try to login to the vault - this may fail if vault is already partially deleted
                Log.Debug("Attempting to login to vault: {VaultName}", _vaultName);
                vault.LoginAuto(_vaultName, 0);
                
                // PDM API doesn't provide a direct method to delete local vault views
                // We need to use alternative methods
                Log.Information("PDM API logged in successfully, but direct vault deletion not supported via API");
                Log.Information("Using alternative deletion methods");
                
                // Since we can't delete via API, try alternative methods
                return TryDeleteVaultViewAlternative();
            }
            catch (COMException comEx) when ((uint)comEx.ErrorCode == 0x8004003B) // E_EDM_LOGIN_FAILED
            {
                Log.Warning("Could not login to vault (already deleted or not configured), attempting alternative deletion");
                // Vault might already be deleted or not properly configured
                // Try alternative method
                return TryDeleteVaultViewAlternative();
            }
            catch (COMException comEx) when ((uint)comEx.ErrorCode == 0x80040065) // E_EDM_NOT_FOUND
            {
                Log.Warning("Vault not found in registry, attempting cleanup");
                return TryDeleteVaultViewAlternative();
            }
            catch (COMException comEx)
            {
                Log.Error(comEx, "COM exception while accessing vault. Error code: 0x{ErrorCode:X8}", (uint)comEx.ErrorCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete vault view using PDM API");
            return false;
        }
        finally
        {
            // Release COM object
            if (vault != null)
            {
                Marshal.ReleaseComObject(vault);
            }
        }
    }

    /// <summary>
    /// Alternative method using PDM View Setup command line
    /// </summary>
    private bool TryDeleteVaultViewAlternative()
    {
        try
        {
            Log.Debug("Attempting alternative vault deletion using PDM command line");
            
            // Try using ViewSetup.exe with delete command
            var viewSetupPath = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM\ViewSetup.exe";
            if (!File.Exists(viewSetupPath))
            {
                viewSetupPath = @"C:\Program Files\SOLIDWORKS PDM\ViewSetup.exe";
            }
            
            if (!File.Exists(viewSetupPath))
            {
                Log.Warning("ViewSetup.exe not found for alternative deletion method");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = viewSetupPath,
                Arguments = $"/Delete \"{_vaultName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(30000); // Wait up to 30 seconds
                
                // Check if vault directory still exists
                Thread.Sleep(1000);
                return !Directory.Exists(_vaultPath);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Alternative vault deletion method failed");
            return false;
        }
    }

    /// <summary>
    /// Uses the PDM registry cleaner tool if available
    /// </summary>
    public bool TryCleanVaultRegistry()
    {
        try
        {
            Log.Debug("Attempting to clean vault registry using PDM tools");
            
            // Look for PDM cleaner tool
            var cleanerPaths = new[]
            {
                @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM\CleanRegistry.exe",
                @"C:\Program Files\SOLIDWORKS PDM\CleanRegistry.exe",
                @"C:\Program Files (x86)\SOLIDWORKS PDM\CleanRegistry.exe"
            };

            string? cleanerPath = cleanerPaths.FirstOrDefault(File.Exists);
            if (cleanerPath == null)
            {
                Log.Debug("PDM registry cleaner not found");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = cleanerPath,
                Arguments = $"/Vault:\"{_vaultName}\" /Silent",
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(10000);
            
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Registry cleaning failed");
            return false;
        }
    }
    
    /// <summary>
    /// Checks if PDM is properly installed
    /// </summary>
    private bool IsPdmInstalled()
    {
        try
        {
            // Check for PDM DLL in common locations
            var pdmPaths = new[]
            {
                @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM\EPDM.Interop.epdm.dll",
                @"C:\Program Files\SOLIDWORKS PDM\EPDM.Interop.epdm.dll",
                @"C:\Program Files (x86)\SOLIDWORKS PDM\EPDM.Interop.epdm.dll"
            };
            
            return pdmPaths.Any(File.Exists);
        }
        catch
        {
            return false;
        }
    }
}