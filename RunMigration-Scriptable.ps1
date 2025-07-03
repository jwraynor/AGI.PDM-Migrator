# AGI PDM Server Migration Tool - Scriptable PowerShell Version
# For use in RMM scripts or automation - runs without user interaction
# Exit codes: 0 = success, 1 = failed, -1 = fatal error

# Set error action preference
$ErrorActionPreference = "Stop"

# Get script directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

# Define paths
$exePath = Join-Path $scriptPath "AGI-PDM\bin\Release\net8.0-windows\AGI-PDM.exe"
$configPath = Join-Path $pwd "config.json"

# Check if exe exists
if (-not (Test-Path $exePath)) {
    Write-Error "AGI-PDM.exe not found at: $exePath"
    exit -1
}

# Log start
Write-Host "Starting AGI PDM Migration Tool..." -ForegroundColor Green
Write-Host "Executable: $exePath"
Write-Host "Config: $configPath"
Write-Host ""

try {
    # Run the migration tool
    $process = Start-Process -FilePath $exePath -WorkingDirectory $pwd -PassThru -Wait -NoNewWindow
    
    # Get exit code
    $exitCode = $process.ExitCode
    
    # Report status
    switch ($exitCode) {
        0 { 
            Write-Host "Migration completed successfully!" -ForegroundColor Green
        }
        1 { 
            Write-Host "Migration failed. Check logs for details." -ForegroundColor Red
        }
        -1 { 
            Write-Host "Fatal error during migration. Check logs for details." -ForegroundColor Red
        }
        default { 
            Write-Host "Migration exited with code: $exitCode" -ForegroundColor Yellow
        }
    }
    
    # Exit with same code
    exit $exitCode
}
catch {
    Write-Error "Failed to run migration: $_"
    exit -1
}