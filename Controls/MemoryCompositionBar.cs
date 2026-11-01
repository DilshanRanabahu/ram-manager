using System.Windows;
using System.Windows.Media;

namespace RamManager.Controls;

/// <summary>Stacked horizontal bar: in use / modified / standby / free.</summary>
public sealed class MemoryCompositionBar : FrameworkElement
{
    private const double Gap = 2;

    public static readonly DependencyProperty InUseProperty = Register(nameof(InUse), 0d);
    public static readonly DependencyProperty ModifiedProperty = Register(nameof(Modified), 0d);
    public static readonly DependencyProperty StandbyProperty = Register(nameof(Standby), 0d);
    public static readonly DependencyProperty FreeProperty = Register(nameof(Free), 0d);

    public static readonly DependencyProperty InUseBrushProperty = Register(nameof(InUseBrush), (Brush)Brushes.DodgerBlue);
    public static readonly DependencyProperty ModifiedBrushProperty = Register(nameof(ModifiedBrush), (Brush)Brushes.Orange);
    public static readonly DependencyProperty StandbyBrushProperty = Register(nameof(StandbyBrush), (Brush)Brushes.MediumPurple);
    public static readonly DependencyProperty FreeBrushProperty = Register(nameof(FreeBrush), (Brush)Brushes.MediumSeaGreen);
    public static readonly DependencyProperty TrackBrushProperty = Register(nameof(TrackBrush), (Brush)Brushes.DimGray);

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(MemoryCompositionBar),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double InUse { get => (double)GetValue(InUseProperty); set => SetValue(InUseProperty, value); }
    public double Modified { get => (double)GetValue(ModifiedProperty); set => SetValue(ModifiedProperty, value); }
    public double Standby { get => (double)GetValue(StandbyProperty); set => SetValue(StandbyProperty, value); }
    public double Free { get => (double)GetValue(FreeProperty); set => SetValue(FreeProperty, value); }

    public Brush InUseBrush { get => (Brush)GetValue(InUseBrushProperty); set => SetValue(InUseBrushProperty, value); }
    public Brush ModifiedBrush { get => (Brush)GetValue(ModifiedBrushProperty); set => SetValue(ModifiedBrushProperty, value); }
    public Brush StandbyBrush { get => (Brush)GetValue(StandbyBrushProperty); set => SetValue(StandbyBrushProperty, value); }
    public Brush FreeBrush { get => (Brush)GetValue(FreeBrushProperty); set => SetValue(FreeBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double radius = height / 2;
        var bounds = new Rect(0, 0, width, height);
        dc.PushClip(new RectangleGeometry(bounds, radius, radius));
        dc.DrawRectangle(TrackBrush, null, bounds);

        var segments = new (double Value, Brush Brush)[]
        {
            (InUse, InUseBrush), (Modified, ModifiedBrush), (Standby, StandbyBrush), (Free, FreeBrush),
        };
        double total = segments.Sum(s => Math.Max(0, s.Value));
        if (total > 0)
        {
            int last = Array.FindLastIndex(segments, s => s.Value > 0);
            double x = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                double value = segments[i].Value;
                if (value <= 0) continue;
                double segmentWidth = width * value / total;
                double drawWidth = i == last ? width - x : Math.Max(0, segmentWidth - Gap);
                dc.DrawRectangle(segments[i].Brush, null, new Rect(x, 0, drawWidth, height));
                x += segmentWidth;
            }
        }

        dc.Pop();
    }
}
