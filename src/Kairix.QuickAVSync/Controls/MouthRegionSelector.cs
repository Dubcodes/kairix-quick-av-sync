using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Controls;

public sealed class MouthRegionSelector : FrameworkElement
{
    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(nameof(Region), typeof(NormalizedMouthRegion), typeof(MouthRegionSelector),
        new FrameworkPropertyMetadata(NormalizedMouthRegion.Default, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty SourcePixelWidthProperty = DependencyProperty.Register(nameof(SourcePixelWidth), typeof(int), typeof(MouthRegionSelector), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SourcePixelHeightProperty = DependencyProperty.Register(nameof(SourcePixelHeight), typeof(int), typeof(MouthRegionSelector), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public NormalizedMouthRegion Region { get => (NormalizedMouthRegion)GetValue(RegionProperty); set => SetValue(RegionProperty, value); }
    public int SourcePixelWidth { get => (int)GetValue(SourcePixelWidthProperty); set => SetValue(SourcePixelWidthProperty, value); }
    public int SourcePixelHeight { get => (int)GetValue(SourcePixelHeightProperty); set => SetValue(SourcePixelHeightProperty, value); }
    private Point? _start;

    public MouthRegionSelector()
    {
        Cursor = Cursors.Cross;
        MouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(this); var display = UniformImageMapping.DisplayRect(ActualWidth, ActualHeight, SourcePixelWidth, SourcePixelHeight);
            if (!display.Contains(point)) return;
            _start = point; CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, e) => { if (_start is { } start && IsMouseCaptured) Update(start, e.GetPosition(this)); };
        MouseLeftButtonUp += (_, e) => { if (_start is { } start) Update(start, e.GetPosition(this)); _start = null; if (IsMouseCaptured) ReleaseMouseCapture(); e.Handled = true; };
    }

    private void Update(Point start, Point end)
    {
        var display = UniformImageMapping.DisplayRect(ActualWidth, ActualHeight, SourcePixelWidth, SourcePixelHeight);
        if (display.IsEmpty) return;
        var clampedStart = UniformImageMapping.Clamp(start, display); var clampedEnd = UniformImageMapping.Clamp(end, display);
        if (Math.Abs(clampedEnd.X - clampedStart.X) < 2 || Math.Abs(clampedEnd.Y - clampedStart.Y) < 2) return;
        Region = UniformImageMapping.ToNormalized(new Rect(clampedStart, clampedEnd), display).Normalize();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var display = UniformImageMapping.DisplayRect(ActualWidth, ActualHeight, SourcePixelWidth, SourcePixelHeight);
        if (display.IsEmpty) return;
        var rect = UniformImageMapping.ToDisplay(Region.Normalize(), display);
        var fill = new SolidColorBrush(Color.FromArgb(35, 0, 220, 255)); fill.Freeze();
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 220, 255)), 2); pen.Freeze(); dc.DrawRectangle(fill, pen, rect);
        var text = new FormattedText("MOUTH ROI · drag to set", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 11, pen.Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(rect.Left + 4, Math.Max(display.Top + 2, rect.Top - text.Height - 2)));
    }
}

public static class UniformImageMapping
{
    public static Rect DisplayRect(double hostWidth, double hostHeight, double sourceWidth, double sourceHeight)
    {
        if (hostWidth <= 0 || hostHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0) return Rect.Empty;
        var scale = Math.Min(hostWidth / sourceWidth, hostHeight / sourceHeight); var width = sourceWidth * scale; var height = sourceHeight * scale;
        return new((hostWidth - width) / 2, (hostHeight - height) / 2, width, height);
    }
    public static Point Clamp(Point point, Rect display) => new(Math.Clamp(point.X, display.Left, display.Right), Math.Clamp(point.Y, display.Top, display.Bottom));
    public static NormalizedMouthRegion ToNormalized(Rect selection, Rect display)
    {
        var clipped = Rect.Intersect(selection, display); if (clipped.IsEmpty || display.IsEmpty) return NormalizedMouthRegion.Default;
        return new((clipped.Left - display.Left) / display.Width, (clipped.Top - display.Top) / display.Height, clipped.Width / display.Width, clipped.Height / display.Height);
    }
    public static Rect ToDisplay(NormalizedMouthRegion region, Rect display)
    {
        var value = region.Normalize(); return new(display.Left + value.X * display.Width, display.Top + value.Y * display.Height, value.Width * display.Width, value.Height * display.Height);
    }
}
