using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;
using Kairix.QuickAVSync.Windows.Capture;
using Kairix.QuickAVSync.Windows.Infrastructure;

namespace Kairix.QuickAVSync.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly AppLogger _log = new();
    private readonly IReadOnlyList<ICaptureBackend> _backends;
    private readonly CaptureDeviceRanker _deviceRanker = new();
    private readonly MemoryStatusService _memoryService = new();
    private readonly TransientDetector _transientDetector = new();
    private readonly VisualClapDetector _visualDetector = new();
    private readonly WaveformBuilder _waveformBuilder = new();
    private readonly AnalysisGeneration _analysisGeneration = new();
    private readonly SessionHistoryService _history = new();
    private RollingBuffer<VideoFrame> _video = new(300, f => f.Timestamp.Ticks100ns);
    private RollingBuffer<AudioChunk> _audio = new(300, a => a.Timestamp.Ticks100ns);
    private ICaptureSession? _capture;
    private CancellationTokenSource? _analysisCts;
    private readonly DispatcherTimer _memoryTimer;
    private AppSettings _settings;
    private CaptureDeviceDescriptor? _selectedDevice;
    private CaptureFormatOption? _selectedCaptureFormat;
    private ImageSource? _videoImage, _autoThumbnail;
    private string _status = "STARTING", _formatText = "No negotiated format", _timingText = "TIMING NOT AVAILABLE", _memoryText = "Reading memory…";
    private bool _isReview, _isHold;
    private IReadOnlyList<VideoFrame> _reviewFrames = [];
    private int _playheadIndex;
    private MediaTimestamp? _audioMark;
    private VisualCandidate? _autoCandidate;
    private MediaTimestamp? _manualVisual;
    private IReadOnlyList<float> _waveform = [];
    private double _systemFraction, _appFraction, _availableFraction;
    private ImageSource? _pendingPreview;
    private int _previewScheduled;
    private bool _suppressFormatReconnect;
    private long _formatRequest;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CaptureDeviceDescriptor> Devices { get; } = [];
    public ObservableCollection<CaptureFormatOption> CaptureFormats { get; } = [];
    public ObservableCollection<SessionResult> RecentResults { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand ManualClapCommand { get; }
    public ICommand ResumeLiveCommand { get; }
    public ICommand StepPreviousCommand { get; }
    public ICommand StepNextCommand { get; }
    public ICommand MarkVisualCommand { get; }
    public ICommand JumpAutoCommand { get; }
    public ICommand CoffeeCommand { get; }

    public MainViewModel()
    {
        _settings = _settingsService.Load(); _backends = [new WindowsCaptureBackend(_log), new SyntheticCaptureBackend()];
        RefreshCommand = new AsyncRelayCommand(RefreshDevicesAsync);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync);
        ManualClapCommand = new RelayCommand(_ => BeginManualClap());
        ResumeLiveCommand = new RelayCommand(_ => ResumeLive());
        StepPreviousCommand = new RelayCommand(_ => Step(-1));
        StepNextCommand = new RelayCommand(_ => Step(1));
        MarkVisualCommand = new RelayCommand(_ => MarkVisual());
        JumpAutoCommand = new RelayCommand(_ => JumpToAuto());
        CoffeeCommand = new RelayCommand(_ => OpenCoffee(), _ => !string.IsNullOrWhiteSpace(AppConstants.BuyMeACoffeeUrl));
        _memoryTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => UpdateMemory(), Application.Current.Dispatcher);
    }

    public CaptureDeviceDescriptor? SelectedDevice { get => _selectedDevice; set { if (Set(ref _selectedDevice, value) && value is not null) { _settings.LastDeviceId = value.Id; _settings.LastDeviceName = value.FriendlyName; SaveSettings(); _ = RefreshCaptureFormatsAsync(value); } } }
    public CaptureFormatOption? SelectedCaptureFormat
    {
        get => _selectedCaptureFormat;
        set
        {
            if (!Set(ref _selectedCaptureFormat, value) || _suppressFormatReconnect || SelectedDevice is null) return;
            if (string.IsNullOrWhiteSpace(value?.Id)) _settings.NativeFormatByDevice.Remove(SelectedDevice.Id);
            else _settings.NativeFormatByDevice[SelectedDevice.Id] = value.Id;
            SaveSettings(); _log.Write("capture.format-selection", $"device='{SelectedDevice.FriendlyName}' selection='{value?.Display ?? CaptureFormatOption.Auto.Display}'");
            _ = ReconnectAsync();
        }
    }
    public bool AutoDetect { get => _settings.AutoDetect; set { if (_settings.AutoDetect != value) { _settings.AutoDetect = value; Changed(); SaveSettings(); } } }
    public bool AutoSpike { get => _settings.AutoSpike; set { if (_settings.AutoSpike != value) { _settings.AutoSpike = value; Changed(); SaveSettings(); } } }
    public bool AutoVisual { get => _settings.AutoVisual; set { if (_settings.AutoVisual != value) { _settings.AutoVisual = value; Changed(); SaveSettings(); } } }
    public double RollingBufferSeconds { get => _settings.RollingBufferSeconds; set { var v = Math.Clamp(value, 1, 30); if (_settings.RollingBufferSeconds != v) { _settings.RollingBufferSeconds = v; ResizeBuffers(); Changed(); SaveSettings(); } } }
    public double WorkWindowMilliseconds { get => _settings.WorkWindowMilliseconds; set { var v = Math.Clamp(value, 50, 2000); if (_settings.WorkWindowMilliseconds != v) { _settings.WorkWindowMilliseconds = v; Changed(); Changed(nameof(HalfWindow)); SaveSettings(); } } }
    public double HalfWindow => WorkWindowMilliseconds;
    public ImageSource? VideoImage { get => _videoImage; private set => Set(ref _videoImage, value); }
    public ImageSource? AutoThumbnail { get => _autoThumbnail; private set => Set(ref _autoThumbnail, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string FormatText { get => _formatText; private set => Set(ref _formatText, value); }
    public string TimingText { get => _timingText; private set => Set(ref _timingText, value); }
    public string MemoryText { get => _memoryText; private set => Set(ref _memoryText, value); }
    public double SystemFraction { get => _systemFraction; private set => Set(ref _systemFraction, value); }
    public double AppFraction { get => _appFraction; private set => Set(ref _appFraction, value); }
    public double AvailableFraction { get => _availableFraction; private set => Set(ref _availableFraction, value); }
    public IReadOnlyList<float> Waveform { get => _waveform; private set => Set(ref _waveform, value); }
    public double AudioMarkerMs => 0;
    public double? AutoVisualMs => _autoCandidate is null || _audioMark is null ? null : (_autoCandidate.Timestamp.Ticks100ns - _audioMark.Value.Ticks100ns) / 10_000d;
    public double? VisualMarkerMs { get { var mark = _manualVisual ?? _autoCandidate?.Timestamp; return mark is null || _audioMark is null ? null : (mark.Value.Ticks100ns - _audioMark.Value.Ticks100ns) / 10_000d; } }
    public double PlayheadMs => _audioMark is null || _reviewFrames.Count == 0 ? 0 : (_reviewFrames[_playheadIndex].Timestamp.Ticks100ns - _audioMark.Value.Ticks100ns) / 10_000d;
    public SyncResult? CurrentResult { get { var mark = _manualVisual ?? _autoCandidate?.Timestamp; return mark is null || _audioMark is null ? null : SyncResult.Calculate(_audioMark.Value, mark.Value); } }
    public string ResultText => CurrentResult?.Wording ?? "WAITING FOR CLAP";
    public double SyncValue => CurrentResult is { TimingComparable: true } result ? result.SignedMilliseconds : 0;
    public string ConfidenceText => _autoCandidate is null ? "No visual candidate" : $"{_autoCandidate.Confidence:P0} confidence · {AutoVisualMs:+0;-0;0} ms";
    public string ReviewPosition => !_isReview || _reviewFrames.Count == 0 ? "LIVE" : $"{_playheadIndex + 1} / {_reviewFrames.Count} · {PlayheadMs:+0.0;-0.0;0} ms";
    // Hold is session-only and must never overwrite the capture readiness state.
    public bool IsHold { get => _isHold; set => Set(ref _isHold, value); }
    public string LogPath => _log.Path;

    public async Task InitializeAsync() { await RefreshDevicesAsync(); if (SelectedDevice is not null) await RefreshCaptureFormatsAsync(SelectedDevice); UpdateMemory(); _memoryTimer.Start(); await ReconnectAsync(); }
    private async Task RefreshDevicesAsync()
    {
        var selected = SelectedDevice?.Id; var found = new List<CaptureDeviceDescriptor>();
        foreach (var backend in _backends)
        {
            try { found.AddRange(await backend.EnumerateDevicesAsync(CancellationToken.None)); }
            catch (Exception ex) { _log.Write("device.enumeration", $"backend={backend.Id} failed: {ex.Message}"); }
        }
        var ranked = _deviceRanker.Rank(found, _settings.LastDeviceId, _settings.LastDeviceName); Devices.Clear(); foreach (var device in ranked) { Devices.Add(device); _log.Write("device.ranking", $"rank={Devices.Count} name='{device.FriendlyName}' kind={device.Kind} available={device.IsAvailable} container='{device.ContainerId ?? "unavailable"}'"); }
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selected) ?? _deviceRanker.SelectBest(found, _settings.LastDeviceId, _settings.LastDeviceName) ?? Devices.FirstOrDefault();
        if (SelectedDevice is not null) await RefreshCaptureFormatsAsync(SelectedDevice);
        _log.Write("device.selection", $"selected='{SelectedDevice?.FriendlyName ?? "none"}' id='{SelectedDevice?.Id ?? "none"}'");
        var hardwareCount = found.Count(d => d.Kind != CaptureDeviceKind.Synthetic); Status = hardwareCount == 0 ? "NO HARDWARE CAPTURE DEVICE — SYNTHETIC MODE" : $"{hardwareCount} CAPTURE DEVICE(S) FOUND";
    }

    private async Task RefreshCaptureFormatsAsync(CaptureDeviceDescriptor device)
    {
        var request = Interlocked.Increment(ref _formatRequest); var options = new List<CaptureFormatOption> { CaptureFormatOption.Auto };
        var backend = _backends.FirstOrDefault(candidate => candidate.Id == device.BackendId);
        if (backend is ICaptureFormatProvider provider)
        {
            try { options.AddRange(await provider.EnumerateFormatsAsync(device, CancellationToken.None)); }
            catch (Exception ex) { _log.Write("capture.format-list", $"device='{device.FriendlyName}' failed: {ex.Message}"); }
        }
        if (request != Volatile.Read(ref _formatRequest) || SelectedDevice?.Id != device.Id) return;
        var saved = _settings.NativeFormatByDevice.TryGetValue(device.Id, out var modeId) ? modeId : null;
        var selected = options.FirstOrDefault(option => string.Equals(option.Id, saved, StringComparison.Ordinal)) ?? CaptureFormatOption.Auto;
        if (!string.IsNullOrWhiteSpace(saved) && selected == CaptureFormatOption.Auto) { _settings.NativeFormatByDevice.Remove(device.Id); SaveSettings(); _log.Write("capture.format-selection", $"device='{device.FriendlyName}' saved native mode is unavailable; falling back to Auto"); }
        _suppressFormatReconnect = true;
        try { CaptureFormats.Clear(); foreach (var option in options) CaptureFormats.Add(option); SelectedCaptureFormat = selected; }
        finally { _suppressFormatReconnect = false; }
    }

    public async Task ReconnectAsync()
    {
        await _reconnectGate.WaitAsync();
        try
        {
        Status = "RECONNECTING"; ClearCurrentMediaState(); await StopCaptureAsync(); _video.Clear(); _audio.Clear();
        if (SelectedDevice is null) { Status = "NO CAPTURE DEVICE"; return; }
        var backend = _backends.FirstOrDefault(b => b.Id == SelectedDevice.BackendId); if (backend is null) { Status = "CAPTURE BACKEND NOT AVAILABLE"; return; }
        try
        {
            _capture = await backend.OpenAsync(SelectedDevice, new(PreferredNativeFormatId: SelectedCaptureFormat?.Id), CancellationToken.None); _capture.VideoSampleReceived += OnVideoFrame; _capture.AudioSampleReceived += OnAudio; _capture.StatusChanged += OnCaptureStatus;
            await _capture.StartAsync(CancellationToken.None); FormatText = $"Capture mode: {_capture.CurrentFormat.Display}"; TimingText = DescribeTiming(_capture.TimingQuality); if (SelectedDevice.Kind == CaptureDeviceKind.Synthetic) Status = "READY TO CLAP"; _log.Write("capture", $"Opened {SelectedDevice.FriendlyName} using {backend.Id}");
        }
        catch (Exception ex) { Status = "CAPTURE OPEN FAILED"; TimingText = "TIMING UNAVAILABLE"; _log.Write("capture", $"Open failed for {SelectedDevice.FriendlyName}: {ex}"); await StopCaptureAsync(); }
        }
        finally { _reconnectGate.Release(); }
    }

    private void ClearCurrentMediaState()
    {
        _analysisGeneration.Next(); _analysisCts?.Cancel(); _isReview = false; _audioMark = null; _manualVisual = null; _autoCandidate = null; _reviewFrames = []; _playheadIndex = 0;
        Interlocked.Exchange(ref _pendingPreview, null); VideoImage = null; AutoThumbnail = null; Waveform = []; FormatText = "Format not negotiated"; TimingText = "TIMING NOT AVAILABLE"; NotifyMarkers();
    }

    private void OnVideoFrame(object? sender, VideoFrame frame)
    {
        _video.Add(frame);
        if (!_isReview) ScheduleLatestPreview(ToBitmap(frame));
    }

    private void ScheduleLatestPreview(ImageSource image)
    {
        Interlocked.Exchange(ref _pendingPreview, image);
        if (Interlocked.Exchange(ref _previewScheduled, 1) != 0) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var latest = Interlocked.Exchange(ref _pendingPreview, null); if (latest is not null && !_isReview) VideoImage = latest;
            Interlocked.Exchange(ref _previewScheduled, 0);
            var newer = Interlocked.Exchange(ref _pendingPreview, null); if (newer is not null) ScheduleLatestPreview(newer);
            if (_capture is not null)
            {
                TimingText = DescribeTiming(_capture.TimingQuality);
                var format = $"Capture mode: {_capture.CurrentFormat.Display}";
                if (FormatText != format) FormatText = format;
            }
        }, DispatcherPriority.Render);
    }

    private void OnAudio(object? sender, AudioChunk chunk)
    {
        _audio.Add(chunk); if (!ReviewTimeline.AcceptsAutomaticEvents(AutoDetect, IsHold)) return;
        foreach (var transient in _transientDetector.Process(chunk))
        {
            _log.Write($"Transient detected at {transient.Timestamp.Ticks100ns}; peak {transient.Peak:0.000}");
            Application.Current.Dispatcher.BeginInvoke(() => StartEvent(transient.Timestamp, AutoSpike));
        }
    }

    private void OnCaptureStatus(object? sender, CaptureStatusChangedEventArgs e)
    {
        _log.Write("capture.status", $"{e.Status}: {e.Message}");
        Application.Current.Dispatcher.BeginInvoke(() => { Status = e.Status switch { CaptureStatus.Starting => e.Message.ToUpperInvariant(), CaptureStatus.Running => e.Message.ToUpperInvariant(), CaptureStatus.DeviceLost => "CAPTURE DEVICE LOST", CaptureStatus.Failed => "CAPTURE FAILED", CaptureStatus.Stopping => "STOPPING", _ => Status }; if (_capture is not null) TimingText = DescribeTiming(_capture.TimingQuality); });
    }

    private async void StartEvent(MediaTimestamp audioMark, bool audioFinalized)
    {
        var generation = _analysisGeneration.Next();
        if (_audioMark is not null && CurrentResult is not null) AddHistory();
        _audioMark = audioMark; _manualVisual = null; _autoCandidate = null; AutoThumbnail = null; _isReview = true; Status = audioFinalized ? "REVIEW · ANALYSING" : "REVIEW · SELECT AUDIO MARK";
        await BuildEventSnapshotAsync(generation); if (!_analysisGeneration.IsCurrent(generation)) return; NotifyMarkers();
        _analysisCts?.Cancel(); _analysisCts = new();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(WorkWindowMilliseconds + 40), _analysisCts.Token);
            await BuildEventSnapshotAsync(generation); if (!_analysisGeneration.IsCurrent(generation)) return;
            if (AutoVisual && audioFinalized)
            {
                var candidate = await _visualDetector.DetectAsync(_reviewFrames, audioMark, _analysisCts.Token);
                if (candidate is not null && _analysisGeneration.IsCurrent(generation)) { _autoCandidate = candidate; AutoThumbnail = ToBitmap(candidate.Frame); _playheadIndex = Math.Max(0, _reviewFrames.IndexOf(candidate.Frame)); _log.Write($"Visual candidate {candidate.Timestamp.Ticks100ns}; confidence {candidate.Confidence:0.00}"); }
            }
            if (!_analysisGeneration.IsCurrent(generation)) return;
            Status = audioFinalized ? "REVIEW" : "REVIEW · SELECT AUDIO MARK"; ShowPlayhead(); NotifyMarkers();
        }
        catch (OperationCanceledException) { }
    }

    private void BeginManualClap()
    {
        var latest = _audio.Snapshot().LastOrDefault(); if (latest is null) return;
        StartEvent(new(latest.Timestamp.Ticks100ns + latest.Duration.Ticks / 2, latest.Timestamp.Quality, latest.Timestamp.ClockDomain), false);
    }
    public void SelectAudioPoint(double relativeMs)
    {
        if (_audioMark is null) return; _audioMark = new(_audioMark.Value.Ticks100ns + (long)(relativeMs * 10_000), _audioMark.Value.Quality, _audioMark.Value.ClockDomain, _audioMark.Value.RawValue); _manualVisual = null; _autoCandidate = null; NotifyMarkers(); StartEvent(_audioMark.Value, true);
        _log.Write($"Manual audio correction {relativeMs:+0.0;-0.0;0} ms");
    }
    public void MovePlayheadTo(double relativeMs)
    {
        if (_audioMark is null || _reviewFrames.Count == 0) return;
        _playheadIndex = ReviewTimeline.NearestFrameIndex(_reviewFrames, _audioMark.Value, relativeMs); ShowPlayhead(); NotifyMarkers();
    }
    private async Task BuildEventSnapshotAsync(long generation)
    {
        if (_audioMark is null) return; var mark = _audioMark.Value; var half = TimeSpan.FromMilliseconds(WorkWindowMilliseconds); var videoSnapshot = _video.Snapshot(); var audioSnapshot = _audio.Snapshot();
        var built = await Task.Run(() =>
        {
            var frames = WorkWindowSelector.Around(videoSnapshot, f => f.Timestamp, mark, half); var chunks = WorkWindowSelector.Around(audioSnapshot, a => a.Timestamp, mark, half + TimeSpan.FromMilliseconds(25));
            return (Frames: frames, Waveform: _waveformBuilder.Build(chunks, mark, half, 900));
        });
        if (!_analysisGeneration.IsCurrent(generation) || _audioMark != mark) return; _reviewFrames = built.Frames; _playheadIndex = _reviewFrames.Count == 0 ? 0 : FindNearest(_reviewFrames, mark); Waveform = built.Waveform; ShowPlayhead();
    }
    private static int FindNearest(IReadOnlyList<VideoFrame> frames, MediaTimestamp mark) => ReviewTimeline.NearestFrameIndex(frames, mark, 0);
    private void Step(int amount) { if (_reviewFrames.Count == 0) return; _playheadIndex = Math.Clamp(_playheadIndex + amount, 0, _reviewFrames.Count - 1); ShowPlayhead(); NotifyMarkers(); }
    public void StepTimeline(int amount) => Step(amount);
    public void StepCoarse(int direction) => Step(direction * 5);
    public void MarkAudioAtPlayhead() { if (_audioMark is not null && _reviewFrames.Count > 0) SelectAudioPoint(PlayheadMs); }
    private void MarkVisual() { if (_reviewFrames.Count == 0) return; _manualVisual = _reviewFrames[_playheadIndex].Timestamp; _log.Write($"Manual visual mark {_manualVisual.Value.Ticks100ns}"); NotifyMarkers(); }
    private void JumpToAuto() { if (_autoCandidate is null || _reviewFrames.Count == 0) return; _playheadIndex = FindNearest(_reviewFrames, _autoCandidate.Timestamp); ShowPlayhead(); NotifyMarkers(); }
    private void ShowPlayhead() { if (_reviewFrames.Count > 0) VideoImage = ToBitmap(_reviewFrames[Math.Clamp(_playheadIndex, 0, _reviewFrames.Count - 1)]); Changed(nameof(ReviewPosition)); Changed(nameof(PlayheadMs)); }
    private void ResumeLive() { _analysisGeneration.Next(); _analysisCts?.Cancel(); if (_audioMark is not null && CurrentResult is not null) AddHistory(); _isReview = false; _audioMark = null; _manualVisual = null; _autoCandidate = null; Waveform = []; AutoThumbnail = null; Status = "READY TO CLAP"; NotifyMarkers(); }
    private void AddHistory() { if (CurrentResult is not { TimingComparable: true } result) return; _history.Add(new(DateTime.Now, result, _autoCandidate?.Confidence)); RecentResults.Clear(); foreach (var item in _history.Items) RecentResults.Add(item); }
    private void NotifyMarkers() { Changed(nameof(AutoVisualMs)); Changed(nameof(VisualMarkerMs)); Changed(nameof(CurrentResult)); Changed(nameof(ResultText)); Changed(nameof(SyncValue)); Changed(nameof(ConfidenceText)); Changed(nameof(PlayheadMs)); Changed(nameof(ReviewPosition)); }
    private static BitmapSource ToBitmap(VideoFrame frame)
    {
        var bitmap = frame.HasPresentation
            ? BitmapSource.Create(frame.PresentationWidth, frame.PresentationHeight, 96, 96, PixelFormats.Bgra32, null, frame.PresentationBgra!, frame.EffectivePresentationStride)
            : BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, frame.Luma, frame.Width);
        bitmap.Freeze(); return bitmap;
    }
    private void ResizeBuffers() { _video.Resize(Math.Max(50, (int)(RollingBufferSeconds * 60))); _audio.Resize(Math.Max(50, (int)(RollingBufferSeconds * 60))); }
    private void UpdateMemory() { var m = _memoryService.Get(); if (m.TotalBytes == 0) return; SystemFraction = m.SystemUsedBytes / (double)m.TotalBytes; AppFraction = m.ProcessBytes / (double)m.TotalBytes; AvailableFraction = m.AvailableBytes / (double)m.TotalBytes; MemoryText = $"System: {Gb(m.SystemUsedBytes):0.0} GB   Kairix: {Gb((ulong)m.ProcessBytes):0.00} GB   Available: {Gb(m.AvailableBytes):0.0} GB"; }
    private static double Gb(ulong bytes) => bytes / 1024d / 1024 / 1024;
    private void SaveSettings() { try { _settingsService.Save(_settings); } catch (Exception ex) { _log.Write($"Settings save failed: {ex.Message}"); } }
    private static void OpenCoffee() { if (!string.IsNullOrWhiteSpace(AppConstants.BuyMeACoffeeUrl)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppConstants.BuyMeACoffeeUrl) { UseShellExecute = true }); }
    private static string DescribeTiming(TimingQuality quality) => quality switch { TimingQuality.DeviceHardware => "DEVICE/QPC TIMESTAMPS", TimingQuality.PlatformCaptureClock => "PLATFORM CAPTURE CLOCK", TimingQuality.ClockCorrelated => "CLOCKS CORRELATED", TimingQuality.StreamTimestamp => "STREAM TIMESTAMPS", TimingQuality.ArrivalFallback => "TIMING DEGRADED · ARRIVAL", _ => "TIMING DOMAINS UNRELATED" };
    private async Task StopCaptureAsync() { if (_capture is null) return; _capture.VideoSampleReceived -= OnVideoFrame; _capture.AudioSampleReceived -= OnAudio; _capture.StatusChanged -= OnCaptureStatus; await _capture.DisposeAsync(); _capture = null; }
    public async ValueTask DisposeAsync() { _memoryTimer.Stop(); _analysisCts?.Cancel(); SaveSettings(); await StopCaptureAsync(); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

internal sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}
internal sealed class AsyncRelayCommand(Func<Task> execute) : ICommand
{
    private bool _running; public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running;
    public async void Execute(object? parameter) { if (_running) return; _running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty); try { await execute(); } finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
}

internal static class ListExtensions { public static int IndexOf<T>(this IReadOnlyList<T> list, T item) { for (var i = 0; i < list.Count; i++) if (EqualityComparer<T>.Default.Equals(list[i], item)) return i; return -1; } }
