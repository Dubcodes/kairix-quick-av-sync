using System.Windows;
using System.Windows.Media;

namespace Kairix.QuickAVSync.Controls;

public sealed class LiveAudioMeter : FrameworkElement
{
    public static readonly DependencyProperty LeftLevelProperty = DependencyProperty.Register(
        nameof(LeftLevel), typeof(double), typeof(LiveAudioMeter), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RightLevelProperty = DependencyProperty.Register(
        nameof(RightLevel), typeof(double), typeof(LiveAudioMeter), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ChannelCountProperty = DependencyProperty.Register(
        nameof(ChannelCount), typeof(int), typeof(LiveAudioMeter), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double LeftLevel { get => (double)GetValue(LeftLevelProperty); set => SetValue(LeftLevelProperty, value); }
    public double RightLevel { get => (double)GetValue(RightLevelProperty); set => SetValue(RightLevelProperty, value); }
    public int ChannelCount { get => (int)GetValue(ChannelCountProperty); set => SetValue(ChannelCountProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var background = TryFindResource("ControlBackgroundBrush") as Brush ?? Brushes.Black;
        var border = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        var fill = TryFindResource("PositiveBrush") as Brush ?? Brushes.DodgerBlue;
        var rows = ChannelCount >= 2 ? 2 : 1;
        const double gap = 3;
        var rowHeight = Math.Max(1, (ActualHeight - gap * (rows - 1)) / rows);
        DrawRow(drawingContext, new Rect(0, 0, ActualWidth, rowHeight), LeftLevel, background, border, fill);
        if (rows == 2) DrawRow(drawingContext, new Rect(0, rowHeight + gap, ActualWidth, rowHeight), RightLevel, background, border, fill);
    }

    private static void DrawRow(DrawingContext context, Rect bounds, double level, Brush background, Brush border, Brush fill)
    {
        context.DrawRoundedRectangle(background, new Pen(border, 1), bounds, 2, 2);
        var inset = new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(0, (bounds.Width - 2) * Math.Clamp(level, 0, 1)), Math.Max(0, bounds.Height - 2));
        if (inset.Width > 0) context.DrawRoundedRectangle(fill, null, inset, 1, 1);
    }
}
