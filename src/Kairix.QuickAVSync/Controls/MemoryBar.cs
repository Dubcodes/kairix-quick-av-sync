using System.Windows;
using System.Windows.Media;

namespace Kairix.QuickAVSync.Controls;

public sealed class MemoryBar : FrameworkElement
{
    public static readonly DependencyProperty OtherUsedFractionProperty = DependencyProperty.Register(nameof(OtherUsedFraction), typeof(double), typeof(MemoryBar), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProcessFractionProperty = DependencyProperty.Register(nameof(ProcessFraction), typeof(double), typeof(MemoryBar), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double OtherUsedFraction { get => (double)GetValue(OtherUsedFractionProperty); set => SetValue(OtherUsedFractionProperty, value); }
    public double ProcessFraction { get => (double)GetValue(ProcessFractionProperty); set => SetValue(ProcessFractionProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        dc.DrawRoundedRectangle(ResourceBrush("ControlBackgroundBrush", Brushes.DimGray), null, bounds, 3, 3);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var other = Math.Clamp(OtherUsedFraction, 0, 1);
        var process = Math.Clamp(ProcessFraction, 0, Math.Max(0, 1 - other));
        var otherWidth = bounds.Width * other; var processWidth = bounds.Width * process;
        dc.PushClip(new RectangleGeometry(bounds, 3, 3));
        dc.DrawRectangle(ResourceBrush("MemoryOtherBrush", Brushes.Gray), null, new Rect(0, 0, otherWidth, bounds.Height));
        if (processWidth > 0)
        {
            var processBrush = ResourceBrush("MemoryProcessBrush", Brushes.Cyan);
            dc.DrawRectangle(processBrush, null, new Rect(otherWidth, 0, processWidth, bounds.Height));
            if (processWidth < 1) dc.DrawRectangle(processBrush, null, new Rect(Math.Clamp(otherWidth, 0, Math.Max(0, bounds.Width - 1)), 0, 1, bounds.Height));
        }
        dc.Pop();
    }
    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
