using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Controls;

public sealed class MouthRegionSelector : FrameworkElement
{
    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region), typeof(NormalizedMouthRegion), typeof(MouthRegionSelector),
        new FrameworkPropertyMetadata(NormalizedMouthRegion.Default,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public NormalizedMouthRegion Region { get => (NormalizedMouthRegion)GetValue(RegionProperty); set => SetValue(RegionProperty, value); }
    private Point? _start;

    public MouthRegionSelector()
    {
        Cursor = Cursors.Cross;
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(this); CaptureMouse(); e.Handled = true; };
        MouseMove += (_, e) => { if (_start is { } start && IsMouseCaptured) Update(start, e.GetPosition(this)); };
        MouseLeftButtonUp += (_, e) => { if (_start is { } start) Update(start, e.GetPosition(this)); _start = null; ReleaseMouseCapture(); e.Handled = true; };
    }

    private void Update(Point start, Point end)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var x = Math.Min(start.X, end.X) / ActualWidth; var y = Math.Min(start.Y, end.Y) / ActualHeight;
        var width = Math.Abs(end.X - start.X) / ActualWidth; var height = Math.Abs(end.Y - start.Y) / ActualHeight;
        if (width >= .02 && height >= .02) Region = new NormalizedMouthRegion(x, y, width, height).Normalize();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var region = Region.Normalize();
        var rect = new Rect(region.X * ActualWidth, region.Y * ActualHeight, region.Width * ActualWidth, region.Height * ActualHeight);
        var fill = new SolidColorBrush(Color.FromArgb(35, 0, 220, 255)); fill.Freeze();
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 220, 255)), 2); pen.Freeze();
        dc.DrawRectangle(fill, pen, rect);
        var text = new FormattedText("MOUTH ROI · drag to set", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 11, pen.Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(rect.Left + 4, Math.Max(2, rect.Top - text.Height - 2)));
    }
}
