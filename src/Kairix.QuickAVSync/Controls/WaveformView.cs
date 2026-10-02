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
    public static readonly DependencyProperty FrameTicksMsProperty = DependencyProperty.Register(nameof(FrameTicksMs), typeof(IReadOnlyList<double>), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<float>? Samples { get => (IReadOnlyList<float>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double HalfWindowMs { get => (double)GetValue(HalfWindowMsProperty); set => SetValue(HalfWindowMsProperty, value); }
    public double AudioMarkerMs { get => (double)GetValue(AudioMarkerMsProperty); set => SetValue(AudioMarkerMsProperty, value); }
    public double? AutoVisualMs { get => (double?)GetValue(AutoVisualMsProperty); set => SetValue(AutoVisualMsProperty, value); }
    public double? VisualMarkerMs { get => (double?)GetValue(VisualMarkerMsProperty); set => SetValue(VisualMarkerMsProperty, value); }
    public double PlayheadMs { get => (double)GetValue(PlayheadMsProperty); set => SetValue(PlayheadMsProperty, value); }
    public IReadOnlyList<double>? FrameTicksMs { get => (IReadOnlyList<double>?)GetValue(FrameTicksMsProperty); set => SetValue(FrameTicksMsProperty, value); }
    public event EventHandler<double>? PlayheadSelected;
    public event EventHandler<double>? AudioPointPreviewed;
    public event EventHandler<double>? AudioPointCommitted;
    public event EventHandler<int>? FrameStepRequested;
    private bool _scrubbing, _draggingAudio;
    private double _pendingAudioMs;

    public WaveformView()
    {
        Cursor = Cursors.Cross;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseWheel += OnMouseWheel;
    }

    private void OnMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this); _draggingAudio = Math.Abs(position.X - MsToX(AudioMarkerMs)) <= 10;
        _scrubbing = !_draggingAudio; _pendingAudioMs = XToMs(position.X); CaptureMouse();
        if (_scrubbing) PlayheadSelected?.Invoke(this, _pendingAudioMs); else { AudioPointPreviewed?.Invoke(this, _pendingAudioMs); InvalidateVisual(); }
        e.Handled = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured) return; var milliseconds = XToMs(e.GetPosition(this).X);
        if (_scrubbing) PlayheadSelected?.Invoke(this, milliseconds);
        else if (_draggingAudio) { _pendingAudioMs = milliseconds; AudioPointPreviewed?.Invoke(this, milliseconds); InvalidateVisual(); }
    }

    private void OnMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return; var milliseconds = XToMs(e.GetPosition(this).X); ReleaseMouseCapture();
        if (_draggingAudio) { _draggingAudio = false; AudioPointCommitted?.Invoke(this, milliseconds); InvalidateVisual(); }
        else if (_scrubbing) PlayheadSelected?.Invoke(this, milliseconds);
        _scrubbing = false; e.Handled = true;
    }

    private void OnMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        FrameStepRequested?.Invoke(this, e.Delta > 0 ? -1 : 1); e.Handled = true;
    }
    private double MsToX(double ms) => ActualWidth * (.5 + ms / (HalfWindowMs * 2));
    private double XToMs(double x) => (x / Math.Max(1, ActualWidth) - .5) * HalfWindowMs * 2;
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(ResourceBrush("PanelSecondaryBrush", Brushes.Black), null, new Rect(0, 0, ActualWidth, ActualHeight), 6, 6);
        var gridBrush = ResourceBrush("GridBrush", Brushes.Gray); var grid = new Pen(gridBrush, 1); var minorGrid = new Pen(gridBrush, .6); var foreground = ResourceBrush("SecondaryTextBrush", Brushes.LightGray);
        for (var i = -10; i <= 10; i++)
        {
            if (i % 2 == 0) continue; var x = MsToX(HalfWindowMs * i / 10); dc.DrawLine(minorGrid, new(x, 42), new(x, ActualHeight - 18));
        }
        for (var i = -5; i <= 5; i++)
        {
            var ms = HalfWindowMs * i / 5; var x = MsToX(ms); dc.DrawLine(grid, new(x, 42), new(x, ActualHeight - 18));
            var text = new FormattedText($"{(ms > 0 ? "+" : "")}{ms:0} ms", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 2, Math.Max(2, ActualWidth - text.Width - 2)), ActualHeight - 16));
        }
        if (FrameTicksMs is { Count: > 0 })
        {
            var framePen = new Pen(foreground, 1);
            foreach (var milliseconds in FrameTicksMs) { var x = MsToX(milliseconds); if (x >= 0 && x <= ActualWidth) dc.DrawLine(framePen, new(x, ActualHeight - 24), new(x, ActualHeight - 18)); }
        }
        if (Samples is { Count: > 1 })
        {
            var baseline = new Pen(gridBrush, 1); var pen = new Pen(ResourceBrush("WaveformBrush", Brushes.Cyan), 1); var mid = (ActualHeight - 18 + 42) / 2d; var amplitude = Math.Max(4, mid - 47);
            dc.DrawLine(baseline, new(0, mid), new(ActualWidth, mid));
            for (var i = 0; i < Samples.Count; i++)
            {
                var x = i / (double)Math.Max(1, Samples.Count - 1) * ActualWidth; var display = Math.Sqrt(Math.Clamp(Samples[i], 0, 1));
                dc.DrawLine(pen, new(x, mid - display * amplitude), new(x, mid + display * amplitude));
            }
        }
        Marker(dc, _draggingAudio ? _pendingAudioMs : AudioMarkerMs, ResourceBrush("AudioMarkerBrush", Brushes.Gold), 2.5, "AUDIO", 2);
        if (AutoVisualMs is { } auto) Marker(dc, auto, ResourceBrush("AutoMarkerBrush", Brushes.DodgerBlue), 1.5, "AUTO", 12);
        if (VisualMarkerMs is { } visual) Marker(dc, visual, ResourceBrush("VisualMarkerBrush", Brushes.Red), 2.5, "VISUAL", 22);
        Marker(dc, PlayheadMs, ResourceBrush("PlayheadBrush", Brushes.White), 1, "PLAYHEAD", 32);
    }
    private void Marker(DrawingContext dc, double ms, Brush brush, double width, string label, double labelTop)
    {
        var x = Math.Clamp(MsToX(ms), 0, ActualWidth); dc.DrawLine(new(brush, width), new(x, 42), new(x, ActualHeight - 18));
        var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 9, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip); dc.DrawText(text, new(Math.Clamp(x + 3, 1, Math.Max(1, ActualWidth - text.Width - 2)), labelTop));
    }
    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
