using RamManager.Services;

namespace RamManager.Helpers;

public static class Log
{
    private static readonly object Gate = new();

    public static void Error(Exception ex, string? context = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsService.FolderPath);
                File.AppendAllText(
                    Path.Combine(SettingsService.FolderPath, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
