using System.Text.Json;
using RamManager.Helpers;

namespace RamManager.Services;

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 2;
    public bool CloseToTray { get; set; } = true;
    public bool ConfirmEndProcess { get; set; } = true;
    public bool TrayHintShown { get; set; }

    // Alerts
    public bool HighMemoryAlerts { get; set; } = true;
    public int HighMemoryThreshold { get; set; } = 85;
    public bool GrowingAppAlerts { get; set; } = true;
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FolderPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RamManager");

    public static string FilePath { get; } = Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
                if (settings.RefreshSeconds is not (1 or 2 or 5)) settings.RefreshSeconds = 2;
                if (settings.HighMemoryThreshold is not (75 or 80 or 85 or 90 or 95)) settings.HighMemoryThreshold = 85;
                return settings;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading settings");
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving settings");
        }
    }
}
