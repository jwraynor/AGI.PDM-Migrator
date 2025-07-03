using System.Security.Principal;
using System.Diagnostics;
using Serilog;

namespace AGI_PDM.Utils;

public static class AdminPrivileges
{
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check administrator privileges");
            return false;
        }
    }

    public static void EnsureAdministratorPrivileges()
    {
        if (!IsRunningAsAdministrator())
        {
            Log.Warning("Application is not running with administrator privileges");
            
            try
            {
                // Try to restart with admin privileges
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine process path"),
                    Verb = "runas",
                    Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1))
                };

                Log.Information("Attempting to restart with administrator privileges...");
                Process.Start(processInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to restart with administrator privileges");
                throw new UnauthorizedAccessException(
                    "This application requires administrator privileges to run. " +
                    "Please right-click the executable and select 'Run as administrator'.", ex);
            }
        }

        Log.Information("Running with administrator privileges");
    }
}