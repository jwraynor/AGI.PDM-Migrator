using Microsoft.Win32;
using Serilog;

namespace AGI_PDM.Services;

public class PdmDetector
{
    public class PdmInstallInfo
    {
        public bool IsInstalled { get; set; }
        public string? InstallPath { get; set; }
        public string? Version { get; set; }
        public string? ViewSetupPath { get; set; }
        public List<string> DetectedLocations { get; set; } = new();
    }

    public static PdmInstallInfo DetectPdmInstallation()
    {
        var info = new PdmInstallInfo();

        // Check registry locations
        CheckRegistry(info);

        // Check common file paths
        CheckFilePaths(info);

        // Check for ViewSetup.exe
        CheckViewSetup(info);

        info.IsInstalled = info.DetectedLocations.Any();
        return info;
    }

    private static void CheckRegistry(PdmInstallInfo info)
    {
        try
        {
            // Check 64-bit registry
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise");
            
            if (key != null)
            {
                info.DetectedLocations.Add("Registry: HKLM\\SOFTWARE\\SolidWorks\\Applications\\PDMWorks Enterprise");
                var versionObj = key.GetValue("Version");
                if (versionObj != null)
                {
                    info.Version = versionObj.ToString();
                }
            }

            // Check 32-bit registry on 64-bit systems
            using var wow64Key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Wow6432Node\SolidWorks\Applications\PDMWorks Enterprise");
            
            if (wow64Key != null)
            {
                info.DetectedLocations.Add("Registry: HKLM\\SOFTWARE\\Wow6432Node\\SolidWorks\\Applications\\PDMWorks Enterprise");
                if (info.Version == null)
                {
                    var versionObj = wow64Key.GetValue("Version");
                    if (versionObj != null)
                    {
                        info.Version = versionObj.ToString();
                    }
                }
            }

            // Check for ConisioAdmin registration
            using var conisioKey = Registry.ClassesRoot.OpenSubKey(@"ConisioAdmin.Application");
            if (conisioKey != null)
            {
                info.DetectedLocations.Add("Registry: ConisioAdmin.Application COM registration");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Registry check failed: {Message}", ex.Message);
        }
    }

    private static void CheckFilePaths(PdmInstallInfo info)
    {
        var pathsToCheck = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SOLIDWORKS PDM"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SOLIDWORKS PDM"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SOLIDWORKS Corp", "SOLIDWORKS PDM"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SOLIDWORKS Corp", "SOLIDWORKS PDM"),
            @"C:\Program Files\SOLIDWORKS PDM",
            @"C:\Program Files (x86)\SOLIDWORKS PDM",
            @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM",
            @"C:\Program Files (x86)\SOLIDWORKS Corp\SOLIDWORKS PDM"
        };

        foreach (var path in pathsToCheck)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    info.DetectedLocations.Add($"Directory: {path}");
                    if (info.InstallPath == null)
                    {
                        info.InstallPath = path;
                    }

                    // Check for key executables
                    var executables = new[] { "ViewSetup.exe", "ConisioAdmin.exe", "EdmServer.exe" };
                    foreach (var exe in executables)
                    {
                        var exePath = Path.Combine(path, exe);
                        if (File.Exists(exePath))
                        {
                            info.DetectedLocations.Add($"Executable: {exePath}");
                            if (exe == "ViewSetup.exe" && info.ViewSetupPath == null)
                            {
                                info.ViewSetupPath = exePath;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to check path {Path}: {Message}", path, ex.Message);
            }
        }
    }

    private static void CheckViewSetup(PdmInstallInfo info)
    {
        try
        {
            // Search PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var paths = pathEnv.Split(';');
                foreach (var path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    
                    var viewSetupPath = Path.Combine(path.Trim(), "ViewSetup.exe");
                    if (File.Exists(viewSetupPath))
                    {
                        info.DetectedLocations.Add($"ViewSetup.exe in PATH: {viewSetupPath}");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PATH search failed: {Message}", ex.Message);
        }
    }
}