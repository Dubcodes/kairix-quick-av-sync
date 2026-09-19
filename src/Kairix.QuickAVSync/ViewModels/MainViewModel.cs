using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly AppLogger _log = new();
    private readonly CaptureDeviceService _devicesService;
    private readonly MemoryStatusService _memoryService = new();
    private readonly TransientDetector _transientDetector = new();
    private readonly VisualClapDetector _visualDetector = new();
    private readonly SessionHistoryService _history = new();
    private RollingBuffer<VideoFrame> _video = new(300, f => f.Timestamp.Ticks100ns);
    private RollingBuffer<AudioChunk> _audio = new(300, a => a.Timestamp.Ticks100ns);
    private ICaptureSession? _capture;
    private CancellationTokenSource? _analysisCts;
    private readonly DispatcherTimer _memoryTimer;
    private AppSettings _settings;
    private CaptureDevice? _selectedDevice;
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

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CaptureDevice> Devices { get; } = [];
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
        _settings = _settingsService.Load(); _devicesService = new(_log);
        RefreshCommand = new RelayCommand(_ => RefreshDevices());
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

    public CaptureDevice? SelectedDevice { get => _selectedDevice; set { if (Set(ref _selectedDevice, value) && value is not null) { _settings.LastDeviceId = value.Id; _settings.LastDeviceName = value.FriendlyName; SaveSettings(); } } }
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
    public double SyncValue => CurrentResult?.SignedMilliseconds ?? 0;
    public string ConfidenceText => _autoCandidate is null ? "No visual candidate" : $"{_autoCandidate.Confidence:P0} confidence · {AutoVisualMs:+0;-0;0} ms";
    public string ReviewPosition => !_isReview || _reviewFrames.Count == 0 ? "LIVE" : $"{_playheadIndex + 1} / {_reviewFrames.Count} · {PlayheadMs:+0.0;-0.0;0} ms";
    public bool IsHold { get => _isHold; set { if (Set(ref _isHold, value)) Status = value ? "HOLD — CAPTURE CONTINUES" : (_isReview ? "REVIEW" : "READY TO CLAP"); } }
    public string LogPath => _log.Path;

    public async Task InitializeAsync() { RefreshDevices(); UpdateMemory(); _memoryTimer.Start(); await ReconnectAsync(); }
    private void RefreshDevices()
    {
        var selected = SelectedDevice?.Id; var real = _devicesService.Enumerate(); Devices.Clear();
        Devices.Add(new("synthetic://development", "Synthetic A/V test source", "synthetic", true, -1));
        foreach (var device in real) Devices.Add(device);
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selected) ?? _devicesService.SelectBest(real, _settings) ?? Devices[0];
        Status = real.Count == 0 ? "NO HARDWARE CAPTURE DEVICE — SYNTHETIC MODE" : $"{real.Count} CAPTURE DEVICE(S) FOUND";
    }

    public async Task ReconnectAsync()
    {
        Status = "RECONNECTING"; await StopCaptureAsync(); _video.Clear(); _audio.Clear();
        if (SelectedDevice is null) { Status = "NO CAPTURE DEVICE"; return; }
        if (!SelectedDevice.Id.StartsWith("synthetic://", StringComparison.OrdinalIgnoreCase))
        {
            Status = "DEVICE ENUMERATED — CAPTURE BACKEND PROVISIONAL";
            TimingText = "TIMING UNAVAILABLE"; _log.Write($"Selected hardware device: {SelectedDevice.FriendlyName}; source-reader capture is not active in this build"); return;
        }
        _capture = new SyntheticCaptureSession(); _capture.VideoFrameReady += OnVideoFrame; _capture.AudioReady += OnAudio;
        await _capture.StartAsync(CancellationToken.None); FormatText = _capture.Format.Display; TimingText = "STREAM TIMESTAMPS · SYNTHETIC"; Status = "READY TO CLAP"; _log.Write("Synthetic capture opened");
    }

    private void OnVideoFrame(VideoFrame frame)
    {
        _video.Add(frame);
        if (!_isReview) Application.Current.Dispatcher.BeginInvoke(() => VideoImage = ToBitmap(frame), DispatcherPriority.Render);
    }

    private void OnAudio(AudioChunk chunk)
    {
        _audio.Add(chunk); if (!AutoDetect || IsHold) return;
        foreach (var transient in _transientDetector.Process(chunk))
        {
            _log.Write($"Transient detected at {transient.Timestamp.Ticks100ns}; peak {transient.Peak:0.000}");
            Application.Current.Dispatcher.BeginInvoke(() => StartEvent(transient.Timestamp, true, AutoSpike));
        }
    }

    private async void StartEvent(MediaTimestamp audioMark, bool detectedAutomatically, bool audioFinalized)
    {
        if (_audioMark is not null && CurrentResult is not null) AddHistory();
        _audioMark = audioMark; _manualVisual = null; _autoCandidate = null; AutoThumbnail = null; _isReview = true; Status = audioFinalized ? "REVIEW · ANALYSING" : "REVIEW · SELECT AUDIO MARK";
        BuildEventSnapshot(); NotifyMarkers();
        _analysisCts?.Cancel(); _analysisCts = new();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(WorkWindowMilliseconds + 40), _analysisCts.Token);
            BuildEventSnapshot();
            if (AutoVisual && audioFinalized)
            {
                var candidate = await _visualDetector.DetectAsync(_reviewFrames, audioMark, _analysisCts.Token);
                if (candidate is not null) { _autoCandidate = candidate; AutoThumbnail = ToBitmap(candidate.Frame); _playheadIndex = Math.Max(0, _reviewFrames.IndexOf(candidate.Frame)); _log.Write($"Visual candidate {candidate.Timestamp.Ticks100ns}; confidence {candidate.Confidence:0.00}"); }
            }
            Status = audioFinalized ? "REVIEW" : "REVIEW · SELECT AUDIO MARK"; ShowPlayhead(); NotifyMarkers();
        }
        catch (OperationCanceledException) { }
    }

    private void BeginManualClap()
    {
        var latest = _audio.Snapshot().LastOrDefault(); if (latest is null) return;
        StartEvent(new(latest.Timestamp.Ticks100ns + latest.Duration.Ticks / 2, latest.Timestamp.Quality), false, false);
    }
    public void SelectAudioPoint(double relativeMs)
    {
        if (_audioMark is null) return; _audioMark = new(_audioMark.Value.Ticks100ns + (long)(relativeMs * 10_000), _audioMark.Value.Quality); _manualVisual = null; _autoCandidate = null; BuildEventSnapshot(); NotifyMarkers(); StartEvent(_audioMark.Value, false, true);
        _log.Write($"Manual audio correction {relativeMs:+0.0;-0.0;0} ms");
    }
    private void BuildEventSnapshot()
    {
        if (_audioMark is null) return; var half = TimeSpan.FromMilliseconds(WorkWindowMilliseconds);
        _reviewFrames = WorkWindowSelector.Around(_video.Snapshot(), f => f.Timestamp, _audioMark.Value, half);
        _playheadIndex = _reviewFrames.Count == 0 ? 0 : FindNearest(_reviewFrames, _audioMark.Value);
        var chunks = WorkWindowSelector.Around(_audio.Snapshot(), a => a.Timestamp, _audioMark.Value, half + TimeSpan.FromMilliseconds(25));
        Waveform = BuildWaveform(chunks, _audioMark.Value, half, 900); ShowPlayhead();
    }
    private static float[] BuildWaveform(IReadOnlyList<AudioChunk> chunks, MediaTimestamp center, TimeSpan half, int points)
    {
        var output = new float[points]; var counts = new int[points]; var start = center.Ticks100ns - half.Ticks; var span = half.Ticks * 2d;
        foreach (var chunk in chunks) for (var i = 0; i < chunk.Samples.Length / chunk.Channels; i++)
        {
            var tick = chunk.Timestamp.Ticks100ns + (long)(i / (double)chunk.SampleRate * TimeSpan.TicksPerSecond); var bin = (int)((tick - start) / span * points); if (bin < 0 || bin >= points) continue;
            float peak = 0; for (var c = 0; c < chunk.Channels; c++) peak = Math.Max(peak, Math.Abs(chunk.Samples[i * chunk.Channels + c])); output[bin] = Math.Max(output[bin], peak); counts[bin]++;
        }
        return output;
    }
    private static int FindNearest(IReadOnlyList<VideoFrame> frames, MediaTimestamp mark) => Enumerable.Range(0, frames.Count).MinBy(i => Math.Abs(frames[i].Timestamp.Ticks100ns - mark.Ticks100ns));
    private void Step(int amount) { if (_reviewFrames.Count == 0) return; _playheadIndex = Math.Clamp(_playheadIndex + amount, 0, _reviewFrames.Count - 1); ShowPlayhead(); NotifyMarkers(); }
    public void StepCoarse(int direction) => Step(direction * 5);
    public void MarkAudioAtPlayhead() { if (_audioMark is not null && _reviewFrames.Count > 0) SelectAudioPoint(PlayheadMs); }
    private void MarkVisual() { if (_reviewFrames.Count == 0) return; _manualVisual = _reviewFrames[_playheadIndex].Timestamp; _log.Write($"Manual visual mark {_manualVisual.Value.Ticks100ns}"); NotifyMarkers(); }
    private void JumpToAuto() { if (_autoCandidate is null || _reviewFrames.Count == 0) return; _playheadIndex = FindNearest(_reviewFrames, _autoCandidate.Timestamp); ShowPlayhead(); NotifyMarkers(); }
    private void ShowPlayhead() { if (_reviewFrames.Count > 0) VideoImage = ToBitmap(_reviewFrames[Math.Clamp(_playheadIndex, 0, _reviewFrames.Count - 1)]); Changed(nameof(ReviewPosition)); Changed(nameof(PlayheadMs)); }
    private void ResumeLive() { _analysisCts?.Cancel(); if (_audioMark is not null && CurrentResult is not null) AddHistory(); _isReview = false; _audioMark = null; _manualVisual = null; _autoCandidate = null; Waveform = []; AutoThumbnail = null; Status = "READY TO CLAP"; NotifyMarkers(); }
    private void AddHistory() { if (CurrentResult is null) return; _history.Add(new(DateTime.Now, CurrentResult, _autoCandidate?.Confidence)); RecentResults.Clear(); foreach (var item in _history.Items) RecentResults.Add(item); }
    private void NotifyMarkers() { Changed(nameof(AutoVisualMs)); Changed(nameof(VisualMarkerMs)); Changed(nameof(CurrentResult)); Changed(nameof(ResultText)); Changed(nameof(SyncValue)); Changed(nameof(ConfidenceText)); Changed(nameof(PlayheadMs)); Changed(nameof(ReviewPosition)); }
    private static BitmapSource ToBitmap(VideoFrame frame) { var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, frame.Luma, frame.Width); bitmap.Freeze(); return bitmap; }
    private void ResizeBuffers() { _video.Resize(Math.Max(50, (int)(RollingBufferSeconds * 60))); _audio.Resize(Math.Max(50, (int)(RollingBufferSeconds * 60))); }
    private void UpdateMemory() { var m = _memoryService.Get(); if (m.TotalBytes == 0) return; SystemFraction = m.SystemUsedBytes / (double)m.TotalBytes; AppFraction = m.ProcessBytes / (double)m.TotalBytes; AvailableFraction = m.AvailableBytes / (double)m.TotalBytes; MemoryText = $"System: {Gb(m.SystemUsedBytes):0.0} GB   Kairix: {Gb((ulong)m.ProcessBytes):0.00} GB   Available: {Gb(m.AvailableBytes):0.0} GB"; }
    private static double Gb(ulong bytes) => bytes / 1024d / 1024 / 1024;
    private void SaveSettings() { try { _settingsService.Save(_settings); } catch (Exception ex) { _log.Write($"Settings save failed: {ex.Message}"); } }
    private static void OpenCoffee() { if (!string.IsNullOrWhiteSpace(AppConstants.BuyMeACoffeeUrl)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppConstants.BuyMeACoffeeUrl) { UseShellExecute = true }); }
    private async Task StopCaptureAsync() { if (_capture is null) return; _capture.VideoFrameReady -= OnVideoFrame; _capture.AudioReady -= OnAudio; await _capture.DisposeAsync(); _capture = null; }
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
