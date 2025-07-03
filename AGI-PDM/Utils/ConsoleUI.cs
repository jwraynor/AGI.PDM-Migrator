using System.Text;
using AGI_PDM.Services;

namespace AGI_PDM.Utils;

public static class ConsoleUI
{
    private const int CONSOLE_WIDTH = 80;
    
    public static void DisplayHeader()
    {
        // ASCII art logo - plain text for stdout
        Console.WriteLine();
        Console.WriteLine(@"       ___   _____ _____   _____  _____  __  __");
        Console.WriteLine(@"      / _ \ / ____|_   _| |  __ \|  __ \|  \/  |");
        Console.WriteLine(@"     / /_\ \ |  __  | |   | |__) | |  | | \  / |");
        Console.WriteLine(@"    / _____ \ | |_ | | |   |  ___/| |  | | |\/| |");
        Console.WriteLine(@"   /_/     \_\____| |_|   |_|    |____/ |_|  |_|");
        Console.WriteLine();
        
        CenterText("Server Migration Tool");
        CenterText("Version 1.0.2");
        Console.WriteLine();
        
        DrawLine('═');
        Console.WriteLine();
    }

    public static void DisplayInfo(string title, string message, ConsoleColor titleColor = ConsoleColor.Yellow)
    {
        Console.WriteLine($"[{title}] {message}");
    }

    public static void DisplaySuccess(string message)
    {
        Console.WriteLine($"[OK] {message}");
    }

    public static void DisplayWarning(string message)
    {
        Console.WriteLine($"[WARNING] {message}");
    }

    public static void DisplayError(string message)
    {
        Console.WriteLine($"[ERROR] {message}");
    }

    public static void DisplaySection(string title)
    {
        Console.WriteLine();
        Console.WriteLine($">>> {title}");
        DrawLine('-', title.Length + 4);
    }

    public static void DisplayProgress(string task, bool isComplete = false)
    {
        if (isComplete)
        {
            Console.WriteLine($"  [DONE] {task}");
        }
        else
        {
            Console.WriteLine($"  [...] {task}");
        }
    }

    public static void DrawLine(char character = '─', int? length = null)
    {
        var lineLength = length ?? CONSOLE_WIDTH;
        Console.WriteLine(new string(character, lineLength));
    }

    public static void CenterText(string text)
    {
        var padding = (CONSOLE_WIDTH - text.Length) / 2;
        if (padding > 0)
        {
            Console.WriteLine($"{new string(' ', padding)}{text}");
        }
        else
        {
            Console.WriteLine(text);
        }
    }

    public static void DisplayBox(string content, ConsoleColor borderColor = ConsoleColor.Gray)
    {
        var lines = content.Split('\n');
        var maxLength = lines.Max(l => l.Length);
        var boxWidth = Math.Min(maxLength + 4, CONSOLE_WIDTH - 2);
        
        // Top border
        Console.WriteLine($"+{new string('-', boxWidth - 2)}+");
        
        // Content
        foreach (var line in lines)
        {
            Console.WriteLine($"| {line.PadRight(boxWidth - 4)} |");
        }
        
        // Bottom border
        Console.WriteLine($"+{new string('-', boxWidth - 2)}+");
    }

    public static void DisplayPdmStatus(PdmDetector.PdmInstallInfo pdmInfo)
    {
        Console.WriteLine();
        if (pdmInfo.IsInstalled)
        {
            DisplaySuccess("SolidWorks PDM installation detected");
            Console.WriteLine();
            
            if (!string.IsNullOrEmpty(pdmInfo.Version))
            {
                DisplayInfo("Version", pdmInfo.Version);
            }
            
            if (!string.IsNullOrEmpty(pdmInfo.InstallPath))
            {
                DisplayInfo("Path", pdmInfo.InstallPath);
            }
            
            if (pdmInfo.DetectedLocations.Any())
            {
                Console.WriteLine();
                Console.WriteLine("  Detection details:");
                foreach (var location in pdmInfo.DetectedLocations.Take(3))
                {
                    Console.WriteLine($"  * {location}");
                }
                if (pdmInfo.DetectedLocations.Count > 3)
                {
                    Console.WriteLine($"  * ... and {pdmInfo.DetectedLocations.Count - 3} more");
                }
            }
        }
        else
        {
            Console.WriteLine();
            DisplayBox(
                "SOLIDWORKS PDM NOT DETECTED\n" +
                "\n" +
                "This migration tool requires SolidWorks PDM Professional\n" +
                "or Standard to be installed on this system.\n" +
                "\n" +
                "The tool could not find:\n" +
                "  - PDM registry entries\n" +
                "  - PDM installation directory\n" +
                "  - ViewSetup.exe executable\n" +
                "\n" +
                "Please ensure SolidWorks PDM is installed before\n" +
                "running this migration tool.\n" +
                "\n" +
                "For more information, contact your IT administrator." +
                "\t .... if you are the IT administrator, please contact James :D"
            );
            Console.WriteLine();
        }
    }

    public static void ShowExitMessage(int exitCode = 0, string? customMessage = null)
    {
        Console.WriteLine();
        DrawLine('═');
        Console.WriteLine();
        
        if (customMessage != null)
        {
            CenterText(customMessage);
        }
        else if (exitCode == 0)
        {
            CenterText("*** Migration completed successfully ***");
        }
        else
        {
            CenterText($"*** Process exited with code: {exitCode} ***");
        }
        
        Console.WriteLine();
        CenterText("AGI PDM Migration Tool - (c) 2024 AGI");
        Console.WriteLine();
    }
}