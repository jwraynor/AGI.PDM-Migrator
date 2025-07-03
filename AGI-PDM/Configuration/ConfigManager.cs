using Newtonsoft.Json;
using Serilog;
using System.Text;

namespace AGI_PDM.Configuration;

public class ConfigManager
{
    private readonly string _configPath;
    private MigrationConfig? _config;

    public ConfigManager(string configPath = "config.json")
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configPath);
    }

    public MigrationConfig LoadConfiguration()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                throw new FileNotFoundException($"Configuration file not found at: {_configPath}");
            }

            var json = File.ReadAllText(_configPath);
            _config = JsonConvert.DeserializeObject<MigrationConfig>(json) 
                ?? throw new InvalidOperationException("Failed to deserialize configuration");

            // Decrypt password if needed
            if (!string.IsNullOrEmpty(_config.Credentials.PdmPassword))
            {
                _config.Credentials.PdmPassword = DecryptPassword(_config.Credentials.PdmPassword);
            }

            ValidateConfiguration(_config);
            return _config;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load configuration from {ConfigPath}", _configPath);
            throw;
        }
    }

    private void ValidateConfiguration(MigrationConfig config)
    {
        var errors = new List<string>();

        // Migration settings validation
        if (string.IsNullOrWhiteSpace(config.Migration.OldServer))
            errors.Add("Old server name is required");

        if (string.IsNullOrWhiteSpace(config.Migration.NewServer))
            errors.Add("New server name is required");

        if (string.IsNullOrWhiteSpace(config.Migration.VaultName))
            errors.Add("Vault name is required");

        if (string.IsNullOrWhiteSpace(config.Migration.VaultPath))
            errors.Add("Vault path is required");

        // Credentials validation
        if (string.IsNullOrWhiteSpace(config.Credentials.PdmUser))
            errors.Add("PDM user is required");

        if (string.IsNullOrWhiteSpace(config.Credentials.PdmPassword))
            errors.Add("PDM password is required");

        // Registry keys validation
        if (string.IsNullOrWhiteSpace(config.RegistryKeys.Primary))
            errors.Add("Primary registry key path is required");

        if (string.IsNullOrWhiteSpace(config.RegistryKeys.Wow64))
            errors.Add("WOW64 registry key path is required");

        // View Setup path validation
        if (!File.Exists(config.Settings.ViewSetupPath))
        {
            errors.Add($"View Setup not found at: {config.Settings.ViewSetupPath}");
        }

        // Create log directory if it doesn't exist
        if (!Directory.Exists(config.Logging.LogPath))
        {
            try
            {
                Directory.CreateDirectory(config.Logging.LogPath);
                Log.Information("Created log directory: {LogPath}", config.Logging.LogPath);
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot create log directory: {ex.Message}");
            }
        }

        if (errors.Any())
        {
            throw new InvalidOperationException($"Configuration validation failed:\n{string.Join("\n", errors)}");
        }
    }

    private string DecryptPassword(string encryptedPassword)
    {
        // Simple base64 decoding for now - in production, use proper encryption
        try
        {
            var bytes = Convert.FromBase64String(encryptedPassword);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // If decryption fails, assume it's plain text
            return encryptedPassword;
        }
    }

    public static string EncryptPassword(string plainPassword)
    {
        // Simple base64 encoding for now - in production, use proper encryption
        var bytes = Encoding.UTF8.GetBytes(plainPassword);
        return Convert.ToBase64String(bytes);
    }
}