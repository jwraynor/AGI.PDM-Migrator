namespace AGI_PDM.Models;

public class MigrationResult
{
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;
    
    public MigrationStepResult PreflightCheck { get; set; } = new();
    public MigrationStepResult DesktopIniUpdate { get; set; } = new();
    public MigrationStepResult RegistryDeletion { get; set; } = new();
    public MigrationStepResult VaultViewDeletion { get; set; } = new();
    public MigrationStepResult ViewSetupExecution { get; set; } = new();
    
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    
    public Dictionary<string, object?> BackupData { get; set; } = new();
}

public class MigrationStepResult
{
    public string StepName { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SkipReason { get; set; }
    public List<string> Details { get; set; } = new();
    
    public void Start(string stepName)
    {
        StepName = stepName;
        StartTime = DateTime.Now;
        Completed = false;
        Success = false;
        Skipped = false;
    }
    
    public void Complete(bool success, string? errorMessage = null)
    {
        EndTime = DateTime.Now;
        Completed = true;
        Success = success;
        ErrorMessage = errorMessage;
    }
    
    public void Skip(string reason)
    {
        EndTime = DateTime.Now;
        Completed = true;
        Success = true; // Skipped steps don't count as failures
        Skipped = true;
        SkipReason = reason;
    }
}