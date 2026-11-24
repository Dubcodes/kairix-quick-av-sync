using System;
using System.IO;
using System.Windows.Input;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.ViewModels;
using Kairix.QuickAVSync.Windows.Infrastructure;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class LipSyncUiTests
{
    private static readonly string Ui = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Kairix.QuickAVSync", "MainWindow.xaml"));

    [Fact]
    public void ExperimentalModeHasExplicitButtonShortcutAndNormalizedRoiOverlay()
    {
        Assert.Contains("Speech / Lip Sync is experimental", Ui);
        Assert.Contains("AutomationProperties.AutomationId=\"AnalyzeLipSyncButton\"", Ui);
        Assert.Contains("Command=\"{Binding AnalyzeLipSyncCommand}\"", Ui);
        Assert.Contains("AutomationProperties.AutomationId=\"MouthRegionSelector\"", Ui);
        Assert.Contains("Region=\"{Binding MouthRegion}\"", Ui);
        Assert.Contains("Set mouth area to analyze lip sync", Ui);
        Assert.Contains("AutomationProperties.AutomationId=\"ResetMouthRegionButton\"", Ui);
        Assert.Contains("For a strong test, say: Pop, baby, pepper, maybe.", Ui);
        Assert.Equal(MainWindowShortcut.AnalyzeLipSync, MainWindow.ResolveShortcut(Key.L, ModifierKeys.None));
    }

    [Fact]
    public void TimelineBindsBoundedLipMarkersAndOneHistorySelectionPath()
    {
        Assert.Contains("LipSyncMarkers=\"{Binding LipSyncMarkers}\"", Ui);
        Assert.Contains("LipSyncMarkerSelected=\"Waveform_LipSyncMarkerSelected\"", Ui);
        Assert.Contains("Command=\"{Binding DataContext.SelectHistoryCommand", Ui);
        Assert.Contains("SelectionCommand=\"{Binding SelectHistoryCommand}\"", Ui);
    }

    [Fact]
    public void AnalysisModeAndPerDeviceRegionRoundTripWithoutMedia()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-lip-settings-{Guid.NewGuid():N}.json");
        try
        {
            using (var service = new SettingsService(path)) service.Save(new AppSettings
            {
                AnalysisMode = AnalysisModeKind.SpeechLipSync,
                MouthRegionByDevice = new() { ["device"] = new(.2, .5, .4, .25) }
            });
            using var reloaded = new SettingsService(path); var settings = reloaded.Load();
            Assert.Equal(AnalysisModeKind.SpeechLipSync, settings.AnalysisMode);
            Assert.Equal(new NormalizedMouthRegion(.2, .5, .4, .25), settings.MouthRegionByDevice["device"]);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("Waveform", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LipSyncMarker", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VideoFrame", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kairix.QuickAVSync.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
