using System.IO;
using System.Runtime.ExceptionServices;
using System.Collections.ObjectModel;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Controls;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;
using Kairix.QuickAVSync.ViewModels;
using Kairix.QuickAVSync.Windows.Infrastructure;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class CaptureModeAndAudioUiTests
{
    private static string Ui => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", "MainWindow.xaml"));

    [Fact]
    public void PrimaryUiSeparatesCaptureModeDriverObservationSourceAndInterpretation()
    {
        Assert.Contains("AutomationProperties.AutomationId=\"CaptureModeSelector\"", Ui);
        Assert.Contains("SelectedItem=\"{Binding SelectedCaptureFormat}\"", Ui);
        Assert.Contains("Text=\"Driver capture\"", Ui);
        Assert.Contains("Text=\"Observed delivery\"", Ui);
        Assert.Contains("Text=\"Source cadence\"", Ui);
        Assert.Contains("AutomationProperties.AutomationId=\"CaptureInterpretationSelector\"", Ui);
        Assert.DoesNotContain("Text=\"Detected\"", Ui);
    }

    [Fact]
    public void CaptureSettingsExposePersistentShowAllModeToggle()
    {
        Assert.Contains("AutomationProperties.AutomationId=\"ShowAllCaptureModesToggle\"", Ui);
        Assert.Contains("IsChecked=\"{Binding ShowAllCaptureModes}\"", Ui);

        var directory = TestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            using var service = new SettingsService(path);
            service.Save(new AppSettings { ShowAllCaptureModes = true });
            Assert.True(service.Load().ShowAllCaptureModes);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void InterpretationTracksOnlyTheSelectedOrNegotiatedTransport()
    {
        RunSta(async () =>
        {
            var directory = TestDirectory();
            try
            {
                var backend = new FakeCaptureBackend();
                await using var viewModel = CreateViewModel(directory, backend);
                await viewModel.InitializeAsync();

                Assert.Equal(CaptureFormatOption.Auto, viewModel.SelectedCaptureFormat);
                Assert.Equal(["25p", "25p → 50i · Top first", "25p → 50i · Bottom first"], viewModel.InterpretationFormats.Select(value => value.ToString()));

                viewModel.SelectedCaptureFormat = viewModel.CaptureModes.Single(option => option.Id == FakeCaptureBackend.Thirty.Id);
                Assert.Equal(["30p"], viewModel.InterpretationFormats.Select(value => value.ToString()));
                Assert.DoesNotContain(viewModel.InterpretationFormats, value => value.TransportRate == Rational.From(25) || value.TransportRate == Rational.From(15));

                viewModel.SelectedCaptureFormat = viewModel.CaptureModes.Single(option => option.Id == FakeCaptureBackend.Recommended.Id);
                Assert.Equal(["25p", "25p → 50i · Top first", "25p → 50i · Bottom first"], viewModel.InterpretationFormats.Select(value => value.ToString()));
                Assert.Equal(FakeCaptureBackend.Recommended.Id, viewModel.SelectedCaptureFormat.Id);
                viewModel.SelectedInterpretationFormat = viewModel.InterpretationFormats.Last();
                Assert.Equal(FakeCaptureBackend.Recommended.Id, viewModel.SelectedCaptureFormat.Id);
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    [Fact]
    public void HistoryTrendGraphBindsDirectlyToTheBoundedHistoryCollection()
    {
        Assert.Contains("AutomationProperties.AutomationId=\"HistoryTrendGraph\"", Ui);
        Assert.Contains("ItemsSource=\"{Binding RecentResults}\"", Ui);
        RunSta(() =>
        {
            var history = new ObservableCollection<SessionResult>();
            var graph = new HistoryTrendGraph { ItemsSource = history };
            Assert.Same(history, graph.ItemsSource);
            Assert.Empty(HistoryTrendSeries.Build(history));
            history.Add(new(DateTime.UtcNow, new(7, "test"), null));
            Assert.Single(HistoryTrendSeries.Build(history));
            history.Clear();
            Assert.Empty(HistoryTrendSeries.Build(history));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ExactModeIsStrictAutoRemainsAutomaticAndAdvancedSelectionStaysVisible()
    {
        RunSta(async () =>
        {
            var directory = TestDirectory();
            try
            {
                var backend = new FakeCaptureBackend();
                await using var viewModel = CreateViewModel(directory, backend);
                await viewModel.InitializeAsync();

                Assert.Equal(CaptureFormatOption.Auto, viewModel.SelectedCaptureFormat);
                Assert.DoesNotContain(viewModel.CaptureModes, option => option.Id == FakeCaptureBackend.Advanced.Id);

                viewModel.ShowAllCaptureModes = true;
                Assert.Contains(viewModel.CaptureModes, option => option.Id == FakeCaptureBackend.Advanced.Id);
                viewModel.SelectedCaptureFormat = viewModel.CaptureModes.Single(option => option.Id == FakeCaptureBackend.Advanced.Id);
                await viewModel.ReconnectAsync();
                Assert.Equal(FakeCaptureBackend.Advanced.Id, backend.LastOptions!.PreferredNativeFormatId);
                Assert.True(backend.LastOptions.RequirePreferredNativeFormat);

                viewModel.ShowAllCaptureModes = false;
                Assert.Contains(viewModel.CaptureModes, option => option.Id == FakeCaptureBackend.Advanced.Id);
                viewModel.SelectedCaptureFormat = null;
                Assert.Equal(FakeCaptureBackend.Advanced.Id, viewModel.SelectedCaptureFormat!.Id);

                viewModel.SelectedCaptureFormat = CaptureFormatOption.Auto;
                await viewModel.ReconnectAsync();
                Assert.False(backend.LastOptions!.RequirePreferredNativeFormat);
                Assert.True(string.IsNullOrEmpty(backend.LastOptions.PreferredNativeFormatId));
                Assert.Equal(CaptureFormatOption.Auto, viewModel.SelectedCaptureFormat);
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    [Fact]
    public void LiveAudioMeterUpdatesWhileHeldAndWhileHistoricalReviewIsDisplayed()
    {
        RunSta(async () =>
        {
            var directory = TestDirectory();
            try
            {
                await using var viewModel = CreateViewModel(directory, new FakeCaptureBackend());
                viewModel.IsHold = true;
                typeof(MainViewModel).GetField("_isReview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(viewModel, true);

                viewModel.AccumulateLiveAudio(new(new(0, TimingQuality.StreamTimestamp), [.5f, .25f], 48_000, 2));
                viewModel.RefreshLiveAudioMeter();

                Assert.Equal(2, viewModel.LiveAudioChannels);
                Assert.Contains("48 kHz · Stereo", viewModel.LiveAudioFormatText);
                Assert.Contains("L -6.0 dBFS", viewModel.LiveAudioLevelText);
                Assert.True(viewModel.AudioLeftLevel > viewModel.AudioRightLevel);
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    [Fact]
    public void LiveAudioMeterIsBoundAndSampleAccumulationPrecedesAutomaticAdmission()
    {
        Assert.Contains("AutomationProperties.AutomationId=\"LiveAudioMeter\"", Ui);
        Assert.Contains("LeftLevel=\"{Binding AudioLeftLevel}\"", Ui);
        var source = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Kairix.QuickAVSync", "ViewModels", "MainViewModel.cs"));
        var audioHandler = source[source.IndexOf("private void OnAudio", StringComparison.Ordinal)..];
        Assert.True(audioHandler.IndexOf("AccumulateLiveAudio(chunk)", StringComparison.Ordinal) < audioHandler.IndexOf("AcceptsAutomaticEvents", StringComparison.Ordinal));
    }

    private static MainViewModel CreateViewModel(string directory, ICaptureBackend backend) =>
        new(new SettingsService(Path.Combine(directory, "settings.json")), new AppLogger(Path.Combine(directory, "current.log")), [backend], applyTheme: false);

    private static string TestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "capture-mode-audio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kairix.QuickAVSync.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception failure) { exception = failure; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null) ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private sealed class FakeCaptureBackend : ICaptureBackend, ICaptureFormatProvider
    {
        private static readonly CaptureFormat RecommendedFormat = new(1920, 1080, Rational.From(25), ScanMode.Progressive, FieldOrder.Unknown, 48_000, VideoPixelFormat.Yuy2);
        private static readonly CaptureFormat ThirtyFormat = new(1920, 1080, Rational.From(30), ScanMode.Progressive, FieldOrder.Unknown, 48_000, VideoPixelFormat.Nv12);
        private static readonly CaptureFormat AdvancedFormat = new(1920, 1080, Rational.From(15), ScanMode.Progressive, FieldOrder.Unknown, 48_000, VideoPixelFormat.Yuy2);
        public static readonly CaptureFormatOption Recommended = new("recommended", "1920×1080 · 25.000p · YUY2", RecommendedFormat);
        public static readonly CaptureFormatOption Thirty = new("thirty", "1920×1080 · 30.000p · NV12", ThirtyFormat);
        public static readonly CaptureFormatOption Advanced = new("advanced", "1920×1080 · 15.000p · YUY2", AdvancedFormat);
        public string Id => "fake";
        public CaptureOpenOptions? LastOptions { get; private set; }

        public Task<IReadOnlyList<CaptureDeviceDescriptor>> EnumerateDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CaptureDeviceDescriptor>>([new("fake-device", "Fake Capture", CaptureDeviceKind.ExternalCapture, Id)]);
        public Task<IReadOnlyList<CaptureFormatOption>> EnumerateFormatsAsync(CaptureDeviceDescriptor device, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CaptureFormatOption>>([Recommended, Thirty, Advanced]);
        public Task<ICaptureSession> OpenAsync(CaptureDeviceDescriptor device, CaptureOpenOptions options, CancellationToken cancellationToken)
        {
            LastOptions = options;
            var format = options.PreferredNativeFormatId switch { "advanced" => AdvancedFormat, "thirty" => ThirtyFormat, _ => RecommendedFormat };
            return Task.FromResult<ICaptureSession>(new FakeCaptureSession(format));
        }
    }

    private sealed class FakeCaptureSession(CaptureFormat format) : ICaptureSession
    {
        public CaptureFormat CurrentFormat { get; } = format;
        public TimingQuality TimingQuality => TimingQuality.StreamTimestamp;
        public event EventHandler<VideoFrame>? VideoSampleReceived { add { } remove { } }
        public event EventHandler<AudioChunk>? AudioSampleReceived { add { } remove { } }
        public event EventHandler<CaptureStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
