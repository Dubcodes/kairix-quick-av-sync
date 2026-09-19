using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Kairix.QuickAVSync.Controls;

public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<float>), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HalfWindowMsProperty = DependencyProperty.Register(nameof(HalfWindowMs), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(250d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AudioMarkerMsProperty = DependencyProperty.Register(nameof(AudioMarkerMs), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AutoVisualMsProperty = DependencyProperty.Register(nameof(AutoVisualMs), typeof(double?), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty VisualMarkerMsProperty = DependencyProperty.Register(nameof(VisualMarkerMs), typeof(double?), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PlayheadMsProperty = DependencyProperty.Register(nameof(PlayheadMs), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<float>? Samples { get => (IReadOnlyList<float>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double HalfWindowMs { get => (double)GetValue(HalfWindowMsProperty); set => SetValue(HalfWindowMsProperty, value); }
    public double AudioMarkerMs { get => (double)GetValue(AudioMarkerMsProperty); set => SetValue(AudioMarkerMsProperty, value); }
    public double? AutoVisualMs { get => (double?)GetValue(AutoVisualMsProperty); set => SetValue(AutoVisualMsProperty, value); }
    public double? VisualMarkerMs { get => (double?)GetValue(VisualMarkerMsProperty); set => SetValue(VisualMarkerMsProperty, value); }
    public double PlayheadMs { get => (double)GetValue(PlayheadMsProperty); set => SetValue(PlayheadMsProperty, value); }
    public event EventHandler<double>? AudioPointSelected;
    public WaveformView() { Cursor = Cursors.Cross; MouseLeftButtonDown += (_, e) => AudioPointSelected?.Invoke(this, XToMs(e.GetPosition(this).X)); }
    private double MsToX(double ms) => ActualWidth * (.5 + ms / (HalfWindowMs * 2));
    private double XToMs(double x) => (x / Math.Max(1, ActualWidth) - .5) * HalfWindowMs * 2;
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(13, 19, 27)), null, new Rect(0, 0, ActualWidth, ActualHeight), 6, 6);
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(44, 57, 72)), 1); var foreground = new SolidColorBrush(Color.FromRgb(151, 166, 183));
        for (var i = -5; i <= 5; i++)
        {
            var ms = HalfWindowMs * i / 5; var x = MsToX(ms); dc.DrawLine(grid, new(x, 22), new(x, ActualHeight - 18));
            var text = new FormattedText($"{(ms > 0 ? "+" : "")}{ms:0} ms", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 2, Math.Max(2, ActualWidth - text.Width - 2)), ActualHeight - 16));
        }
        if (Samples is { Count: > 1 })
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(91, 209, 191)), 1.2); var mid = (ActualHeight - 18 + 22) / 2;
            for (var i = 1; i < Samples.Count; i++) dc.DrawLine(pen, new((i - 1d) / (Samples.Count - 1) * ActualWidth, mid - Samples[i - 1] * (mid - 26)), new(i / (double)(Samples.Count - 1) * ActualWidth, mid - Samples[i] * (mid - 26)));
        }
        Marker(dc, AudioMarkerMs, Color.FromRgb(250, 204, 74), 2.5, "AUDIO");
        if (AutoVisualMs is { } auto) Marker(dc, auto, Color.FromRgb(105, 164, 255), 1.5, "AUTO");
        if (VisualMarkerMs is { } visual) Marker(dc, visual, Color.FromRgb(255, 111, 107), 2.5, "VISUAL");
        Marker(dc, PlayheadMs, Colors.White, 1, "PLAYHEAD");
    }
    private void Marker(DrawingContext dc, double ms, Color color, double width, string label)
    {
        var x = Math.Clamp(MsToX(ms), 0, ActualWidth); var brush = new SolidColorBrush(color); dc.DrawLine(new(brush, width), new(x, 18), new(x, ActualHeight - 18));
        var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 9, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip); dc.DrawText(text, new(Math.Clamp(x + 3, 1, Math.Max(1, ActualWidth - text.Width - 2)), 2));
    }
}
