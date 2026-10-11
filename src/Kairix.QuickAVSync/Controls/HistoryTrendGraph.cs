using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Controls;

public sealed class HistoryTrendGraph : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(HistoryTrendGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
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
        if (points.Count == 0)
        {
            DrawEmptyState(context, bounds, mutedBrush);
            return;
        }

        const double horizontalPadding = 9;
        const double verticalPadding = 9;
        var width = Math.Max(1, bounds.Width - horizontalPadding * 2);
        var height = Math.Max(1, bounds.Height - verticalPadding * 2);
        var zeroY = verticalPadding + height / 2;
        var zeroPen = new Pen(borderBrush, 1) { DashStyle = DashStyles.Dash };
        context.DrawLine(zeroPen, new(horizontalPadding, zeroY), new(horizontalPadding + width, zeroY));

        var range = Math.Max(10, points.Max(point => Math.Abs(point.Milliseconds)) * 1.15);
        Point Map(HistoryTrendPoint point)
        {
            var xFraction = point.TotalPositions <= 1 ? 1 : point.Position / (double)(point.TotalPositions - 1);
            var yFraction = Math.Clamp(point.Milliseconds / range, -1, 1);
            return new(horizontalPadding + width * xFraction, zeroY - yFraction * height * .45);
        }

        var linePen = new Pen(accentBrush, 1.25);
        HistoryTrendPoint? previous = null;
        foreach (var point in points)
        {
            var currentPosition = Map(point);
            if (previous is { } prior && point.Position == prior.Position + 1)
                context.DrawLine(linePen, Map(prior), currentPosition);
            context.DrawEllipse(accentBrush, null, currentPosition, point.IsNewest ? 4 : 3, point.IsNewest ? 4 : 3);
            previous = point;
        }
    }

    private static void OnItemsSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var graph = (HistoryTrendGraph)owner;
        if (args.OldValue is INotifyCollectionChanged oldCollection)
            CollectionChangedEventManager.RemoveHandler(oldCollection, graph.OnCollectionChanged);
        if (args.NewValue is INotifyCollectionChanged newCollection)
            CollectionChangedEventManager.AddHandler(newCollection, graph.OnCollectionChanged);
        graph.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => InvalidateVisual();

    private void DrawEmptyState(DrawingContext context, Rect bounds, Brush brush)
    {
        const string text = "No comparable history yet";
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, new((bounds.Width - formatted.Width) / 2, (bounds.Height - formatted.Height) / 2));
    }
}
