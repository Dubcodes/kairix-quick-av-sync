using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Controls;

public sealed class HistoryTrendGraph : FrameworkElement
{
    private const double HitRadius = 8;
    private readonly List<(Point Center, HistoryTrendPoint Point)> _hitTargets = [];

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(HistoryTrendGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectionCommandProperty = DependencyProperty.Register(
        nameof(SelectionCommand), typeof(ICommand), typeof(HistoryTrendGraph));

    public ICommand? SelectionCommand
    {
        get => (ICommand?)GetValue(SelectionCommandProperty);
        set => SetValue(SelectionCommandProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var borderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        var accentBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var mutedBrush = TryFindResource("MutedTextBrush") as Brush ?? Brushes.Gray;
        context.DrawRoundedRectangle(Brushes.Transparent, new Pen(borderBrush, 1), bounds, 4, 4);

        var results = ItemsSource?.Cast<object>().OfType<SessionResult>() ?? [];
        var points = HistoryTrendSeries.Build(results);
        _hitTargets.Clear();
        if (points.Count == 0)
        {
            DrawEmptyState(context, bounds, mutedBrush);
            return;
        }

        const double horizontalPadding = 9;
        const double verticalPadding = 9;
        var range = HistoryTrendSeries.NiceRange(points.Select(point => point.Milliseconds));
        var upperLabel = FormatScaleLabel(range);
        var zeroLabel = FormatScaleLabel(0);
        var lowerLabel = FormatScaleLabel(-range);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var upperText = ScaleText(upperLabel, mutedBrush, pixelsPerDip);
        var zeroText = ScaleText(zeroLabel, mutedBrush, pixelsPerDip);
        var lowerText = ScaleText(lowerLabel, mutedBrush, pixelsPerDip);
        var labelWidth = Math.Max(upperText.Width, Math.Max(zeroText.Width, lowerText.Width));
        const double labelGap = 6;
        var width = Math.Max(1, bounds.Width - horizontalPadding * 2 - labelGap - labelWidth);
        var height = Math.Max(1, bounds.Height - verticalPadding * 2);
        var zeroY = verticalPadding + height / 2;
        var amplitude = height * .45;
        var plotRight = horizontalPadding + width;
        var zeroPen = new Pen(borderBrush, 1) { DashStyle = DashStyles.Dash };
        context.DrawLine(zeroPen, new(horizontalPadding, zeroY), new(plotRight, zeroY));
        var labelX = plotRight + labelGap;
        context.DrawText(upperText, new(labelX, zeroY - amplitude - upperText.Height / 2));
        context.DrawText(zeroText, new(labelX, zeroY - zeroText.Height / 2));
        context.DrawText(lowerText, new(labelX, zeroY + amplitude - lowerText.Height / 2));

        Point Map(HistoryTrendPoint point)
        {
            var xFraction = point.TotalPositions <= 1 ? 1 : point.Position / (double)(point.TotalPositions - 1);
            var yFraction = Math.Clamp(point.Milliseconds / range, -1, 1);
            return new(horizontalPadding + width * xFraction, zeroY - yFraction * amplitude);
        }

        var linePen = new Pen(accentBrush, 1.25);
        HistoryTrendPoint? previous = null;
        foreach (var point in points)
        {
            var currentPosition = Map(point);
            if (previous is { } prior && point.Position == prior.Position + 1)
                context.DrawLine(linePen, Map(prior), currentPosition);
            context.DrawEllipse(accentBrush, null, currentPosition, point.IsNewest ? 4 : 3, point.IsNewest ? 4 : 3);
            _hitTargets.Add((currentPosition, point));
            previous = point;
        }
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        var hit = FindHit(args.GetPosition(this));
        ToolTip = hit is null ? null : HistoryTrendSeries.FormatTooltip(hit.Value.Point.Milliseconds);
        Cursor = hit is null ? null : Cursors.Hand;
    }

    protected override void OnMouseLeave(MouseEventArgs args)
    {
        base.OnMouseLeave(args);
        ToolTip = null;
        Cursor = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonDown(args);
        var hit = FindHit(args.GetPosition(this));
        if (hit is not null && ExecutePoint(hit.Value.Point)) args.Handled = true;
    }

    internal bool ExecutePoint(HistoryTrendPoint point)
    {
        var result = ItemsSource?.Cast<object>().OfType<SessionResult>().Take(HistoryTrendSeries.MaximumPoints)
            .ElementAtOrDefault(point.NewestFirstIndex);
        if (result is null || SelectionCommand is null || !SelectionCommand.CanExecute(result)) return false;
        SelectionCommand.Execute(result);
        return true;
    }

    private (Point Center, HistoryTrendPoint Point)? FindHit(Point position)
    {
        (Point Center, HistoryTrendPoint Point)? nearest = null;
        var distanceSquared = HitRadius * HitRadius;
        foreach (var target in _hitTargets)
        {
            var dx = target.Center.X - position.X; var dy = target.Center.Y - position.Y;
            var candidateDistance = dx * dx + dy * dy;
            if (candidateDistance > distanceSquared) continue;
            distanceSquared = candidateDistance;
            nearest = target;
        }
        return nearest;
    }

    private static void OnItemsSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var graph = (HistoryTrendGraph)owner;
        if (args.OldValue is INotifyCollectionChanged oldCollection)
            CollectionChangedEventManager.RemoveHandler(oldCollection, graph.OnCollectionChanged);
        if (args.NewValue is INotifyCollectionChanged newCollection)
            CollectionChangedEventManager.AddHandler(newCollection, graph.OnCollectionChanged);
        graph._hitTargets.Clear();
        graph.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) { _hitTargets.Clear(); InvalidateVisual(); }

    private void DrawEmptyState(DrawingContext context, Rect bounds, Brush brush)
    {
        const string text = "No comparable history yet";
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, new((bounds.Width - formatted.Width) / 2, (bounds.Height - formatted.Height) / 2));
    }

    private static string FormatScaleLabel(double milliseconds) => milliseconds switch
    {
        > 0 => $"+{milliseconds:0} ms",
        < 0 => $"−{Math.Abs(milliseconds):0} ms",
        _ => "0 ms"
    };

    private static FormattedText ScaleText(string text, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 9, brush, pixelsPerDip);
}
