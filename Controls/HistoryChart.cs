using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RamManager.Controls;

/// <summary>Lightweight area chart of percentage values (0–100), newest on the right.</summary>
public sealed class HistoryChart : FrameworkElement
{
    private const double LabelWidth = 40;
    private const double VerticalPadding = 8;

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(HistoryChart),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(SolidColorBrush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
    public SolidColorBrush LineBrush { get => (SolidColorBrush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= LabelWidth || height <= VerticalPadding * 2) return;

        double plotLeft = LabelWidth;
        double plotWidth = width - plotLeft;
        double plotTop = VerticalPadding;
        double plotHeight = height - VerticalPadding * 2;
        double plotBottom = plotTop + plotHeight;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Grid lines and labels
        var gridPen = new Pen(GridBrush, 1);
        gridPen.Freeze();
        for (int percent = 0; percent <= 100; percent += 25)
        {
            double y = Math.Round(plotTop + plotHeight * (1 - percent / 100.0)) + 0.5;
            dc.DrawLine(gridPen, new Point(plotLeft, y), new Point(width, y));

            var label = new FormattedText($"{percent}%", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                LabelTypeface, 11, LabelBrush, pixelsPerDip);
            dc.DrawText(label, new Point(plotLeft - 10 - label.Width, y - label.Height / 2));
        }

        var values = Values;
        if (values is null || values.Count < 2) return;

        int capacity = Math.Max(Capacity, values.Count);
        double step = plotWidth / (capacity - 1);
        int offset = capacity - values.Count;

        Point PointAt(int i) => new(
            plotLeft + (i + offset) * step,
            plotTop + plotHeight * (1 - Math.Clamp(values[i], 0, 100) / 100));

        var points = new Point[values.Count];
        for (int i = 0; i < values.Count; i++) points[i] = PointAt(i);

        // Filled area
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, plotBottom), true, true);
            ctx.PolyLineTo(points, false, true);
            ctx.LineTo(new Point(points[^1].X, plotBottom), false, false);
        }
        area.Freeze();

        var color = LineBrush.Color;
        var fill = new LinearGradientBrush(
            Color.FromArgb(0x55, color.R, color.G, color.B),
            Color.FromArgb(0x00, color.R, color.G, color.B),
            90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        // Line
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points[1..], true, true);
        }
        line.Freeze();

        var linePen = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round };
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, line);

        // Latest value marker
        var halo = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
        halo.Freeze();
        dc.DrawEllipse(halo, null, points[^1], 8, 8);
        dc.DrawEllipse(LineBrush, null, points[^1], 4, 4);
    }
}
