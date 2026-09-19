using System.Windows;
using System.Windows.Media;

namespace Kairix.QuickAVSync.Controls;

public sealed class SyncBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SyncBar), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(nameof(Range), typeof(double), typeof(SyncBar), new FrameworkPropertyMetadata(250d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Range { get => (double)GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        var middle = ActualWidth / 2; var y = ActualHeight / 2; var label = new SolidColorBrush(Color.FromRgb(162, 176, 193));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(32, 43, 56)), null, new(0, y - 7, ActualWidth, 14), 7, 7);
        var extent = Math.Min(middle, Math.Abs(Value) / Math.Max(1, Range) * middle);
        var brush = new SolidColorBrush(Value >= 0 ? Color.FromRgb(53, 127, 235) : Color.FromRgb(226, 70, 70));
        dc.DrawRectangle(brush, null, Value >= 0 ? new(middle - extent, y - 7, extent, 14) : new(middle, y - 7, extent, 14));
        dc.DrawLine(new Pen(Brushes.White, 2), new(middle, y - 12), new(middle, y + 12));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawText(new("AUDIO LEADS", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 10, new SolidColorBrush(Color.FromRgb(98, 158, 255)), dpi), new(0, 0));
        var lag = new FormattedText("AUDIO LAGS", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 10, new SolidColorBrush(Color.FromRgb(255, 102, 102)), dpi); dc.DrawText(lag, new(ActualWidth - lag.Width, 0));
    }
}
