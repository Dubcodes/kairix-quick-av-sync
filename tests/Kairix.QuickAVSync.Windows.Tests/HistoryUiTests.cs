using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;
using Kairix.QuickAVSync.ViewModels;
using Kairix.QuickAVSync.Windows.Infrastructure;
using System.IO;
using System.Windows.Input;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class HistorySettingsAndUiTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void HistoryDepthPreferenceRoundTripsOnlySupportedValues(int depth)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-history-{Guid.NewGuid():N}.json");
        try
        {
            using var service = new SettingsService(path);
            service.Save(new AppSettings { SessionHistoryDepth = depth });
            Assert.Equal(depth, service.Load().SessionHistoryDepth);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UnsupportedHistoryDepthLoadsAsDefaultThree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-history-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"SessionHistoryDepth\":7}");
            using var service = new SettingsService(path);
            Assert.Equal(3, service.Load().SessionHistoryDepth);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void HistoryItemsAreClickableAndDepthControlIsBoundWithoutChangingLiveViewSemantics()
    {
        var ui = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", "MainWindow.xaml"));

        Assert.Contains("AutomationProperties.AutomationId=\"SessionHistoryDepthSelector\"", ui);
        Assert.Contains("ItemsSource=\"{Binding SessionHistoryDepths}\" SelectedItem=\"{Binding SessionHistoryDepth}\"", ui);
        Assert.Contains("AutomationProperties.AutomationId=\"HistoryResultButton\"", ui);
        Assert.Contains("Command=\"{Binding DataContext.SelectHistoryCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", ui);
        Assert.Contains("CommandParameter=\"{Binding}\"", ui);
        Assert.Contains("Hold automatic detection and review this retained event", ui);
        Assert.Contains("Select an event to reopen its retained review", ui);
        Assert.Contains("Text=\"OPEN  ›\"", ui);
        Assert.Contains("Command=\"{Binding ResumeLiveCommand}\"", ui);
        Assert.Equal(typeof(ICommand), typeof(MainViewModel).GetProperty(nameof(MainViewModel.SelectHistoryCommand))!.PropertyType);
    }

    [Fact]
    public void HistoricalResultHasAnExplicitLabelAndEditingUsesAnIndependentManualPreview()
    {
        var frame = new VideoFrame(T(40), 1, 1, [0], 2);
        var review = new EventReviewState(T(0), T(0));
        review.SetHistoryCandidate(new(frame.Timestamp, frame.TemporalIndex, .8, 4, frame));

        Assert.Equal("HISTORY REVIEW", ReviewMeasurementPresentation.ResultModeText(null, review.ResultMode));
        review.MovePlayhead(T(60));
        Assert.Equal("MANUAL PREVIEW", ReviewMeasurementPresentation.ResultModeText(null, review.ResultMode));
    }

    private static MediaTimestamp T(double milliseconds) =>
        new((long)Math.Round(milliseconds * 10_000), TimingQuality.StreamTimestamp);
}
