using System.Windows;
using System.Windows.Media;

namespace Kairix.QuickAVSync.Services;

public sealed record ThemeOption(string Name)
{
    public override string ToString() => Name;
}

public static class ThemeManager
{
    public static IReadOnlyList<ThemeOption> Themes { get; } = [new("Graphite"), new("Midnight"), new("Light"), new("High Contrast"), new("Synthwave"), new("Terminal"), new("Solar Flare")];

    public static string Normalize(string? name) => Themes.Any(theme => theme.Name == name) ? name! : "Graphite";

    public static void Apply(string? name)
    {
        var normalized = Normalize(name);
        var file = normalized.Replace(" ", "", StringComparison.Ordinal);
        var dictionary = new ResourceDictionary { Source = new Uri($"Themes/{file}.xaml", UriKind.Relative) };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0) merged.Add(dictionary); else merged[0] = dictionary;
        foreach (Window window in Application.Current.Windows) Invalidate(window);
    }

    private static void Invalidate(DependencyObject value)
    {
        if (value is UIElement element) element.InvalidateVisual();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(value); index++) Invalidate(VisualTreeHelper.GetChild(value, index));
    }
}
