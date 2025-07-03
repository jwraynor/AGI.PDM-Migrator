namespace AGI_PDM.Configuration;

public class MigrationConfig
{
    public MigrationSettings Migration { get; set; } = new();
    public CredentialSettings Credentials { get; set; } = new();
    public RegistryKeySettings RegistryKeys { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

public class MigrationSettings
{
    public string OldServer { get; set; } = string.Empty;
    public string NewServer { get; set; } = string.Empty;
    public int NewServerPort { get; set; } = 3030;
    public string VaultName { get; set; } = string.Empty;
    public string VaultPath { get; set; } = string.Empty;
    public bool DeleteLocalCache { get; set; } = true;
}

public class CredentialSettings
{
    public string PdmUser { get; set; } = string.Empty;
    public string PdmPassword { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public bool UseCurrentUserForVault { get; set; } = true;
    public string VaultOwnerOverride { get; set; } = string.Empty; // Optional: specify the actual user for desktop.ini
}

public class RegistryKeySettings
{
    public string Primary { get; set; } = string.Empty;
    public string Wow64 { get; set; } = string.Empty;
}

public class AppSettings
{
    public bool VerifyCheckedIn { get; set; } = true;
    public bool BackupRegistry { get; set; } = true;
    public bool RequireAdminRights { get; set; } = true;
    public bool AutoRestartAsAdmin { get; set; } = true;
    public string ViewSetupPath { get; set; } = @"C:\Program Files\SOLIDWORKS PDM\ViewSetup.exe";
}

public class LoggingSettings
{
    public string LogPath { get; set; } = string.Empty;
    public string LogLevel { get; set; } = "Information";
    public bool CreateDetailedLog { get; set; } = true;
}