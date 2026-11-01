using System.Windows;
using System.Windows.Media;

namespace RamManager.Helpers;

/// <summary>Access to theme brushes from view models.</summary>
public static class Palette
{
    public static Brush Get(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>A translucent version of a theme brush, used for chips and icon backgrounds.</summary>
    public static Brush Soft(string key, byte alpha = 0x26)
    {
        var color = ((SolidColorBrush)Get(key)).Color;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
