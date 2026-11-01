namespace RamManager.Helpers;

public static class Format
{
    private const double KB = 1024, MB = KB * 1024, GB = MB * 1024;

    /// <summary>Human friendly size, e.g. "512 MB", "1.25 GB".</summary>
    public static string Bytes(double bytes)
    {
        double abs = Math.Abs(bytes);
        if (abs >= GB) return (bytes / GB).ToString("0.0#") + " GB";
        if (abs >= MB) return (bytes / MB).ToString("0") + " MB";
        if (abs >= KB) return (bytes / KB).ToString("0") + " KB";
        return bytes.ToString("0") + " B";
    }

    public static string Gb(double bytes) => (bytes / GB).ToString("0.0") + " GB";

    public static string Mb(double bytes) => (bytes / MB).ToString("N1") + " MB";

    public static string Percent(double value) => value.ToString("0") + "%";
}
