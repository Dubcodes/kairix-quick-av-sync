using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Reflection;
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
    private readonly ObservedSignalAnalyzer _signalAnalyzer = new();
    private RollingBuffer<VideoFrame> _video = new(300, f => f.Timestamp.Ticks100ns);
    private RollingBuffer<AudioChunk> _audio = new(300, a => a.Timestamp.Ticks100ns);
    private ICaptureSession? _capture;
    private CancellationTokenSource? _analysisCts;
    private readonly DispatcherTimer _memoryTimer;
    private AppSettings _settings;
    private CaptureDeviceDescriptor? _selectedDevice;
    private CaptureFormatOption? _selectedCaptureFormat;
    private ResolutionOption? _selectedResolution;
    private InterpretationFormatOption? _selectedInterpretationFormat;
    private PixelFormatOption? _selectedPixelFormat;
    private ProcessingResolutionOption? _selectedDetectionResolution, _selectedReviewResolution;
    private ThemeOption? _selectedTheme;
    private string _selectedAudioDisplayStyle = "Centered Fill";
    private string _selectedAudioDisplayAmplitude = WaveformDisplayTransform.AutoGain;
    private bool _reconstructInterlacedFields;
    private InputSignalInfo? _observedSignal;
    private ObservedSignalAnalysis? _observedAnalysis;
    private double? _observedDeliveredRate;
    private ImageSource? _videoImage, _autoThumbnail;
    private string _status = "STARTING", _formatText = "No negotiated format", _timingText = "TIMING NOT AVAILABLE", _memoryText = "Reading memory…";
    private bool _isReview, _isHold;
    private IReadOnlyList<VideoFrame> _reviewFrames = [];
    private int _playheadIndex;
    private EventReviewState? _review;
    private IReadOnlyList<float> _waveform = [];
    private IReadOnlyList<double> _reviewFrameTicks = [];
    private FrameTimingAnalysis? _timelineAnalysis;
    private double _systemFraction, _otherSystemFraction, _appFraction, _availableFraction;
    private VideoFrame? _pendingPreview;
    private int _previewScheduled;
    private bool _suppressInterpretationChanges;
    private bool _suppressDeviceRefresh;
    private int _signalAnalysisRunning;
    private long _lastSignalAnalysisUtcTicks;
    private long _formatRequest;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private int _disposing;
    private IReadOnlyList<CaptureFormatOption> _allCaptureFormats = [CaptureFormatOption.Auto];
    private CaptureStatus _captureStatus = CaptureStatus.Created;
    private string _captureStatusText = "CAPTURE NOT STARTED";
    private string _lastEventDiagnosticsText = "No detected event diagnostics yet.";
    private long _lastAudioEndTicks = long.MinValue;
    private int _audioContinuityFaults;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CaptureDeviceDescriptor> Devices { get; } = [];
    public ObservableCollection<ResolutionOption> Resolutions { get; } = [];
    public ObservableCollection<InterpretationFormatOption> InterpretationFormats { get; } = [];
    public ObservableCollection<PixelFormatOption> PixelFormats { get; } = [];
    public ObservableCollection<ProcessingResolutionOption> DetectionResolutions { get; } = [];
    public ObservableCollection<ProcessingResolutionOption> ReviewResolutions { get; } = [];
    public ObservableCollection<ThemeOption> Themes { get; } = [];
    public ObservableCollection<string> AudioDisplayStyles { get; } = ["Centered Fill", "Centered Bars", "Centered Line", "Peak Fill", "Peak Bars", "Peak Line"];
    public ObservableCollection<string> AudioDisplayAmplitudes { get; } = [WaveformDisplayTransform.AutoGain, WaveformDisplayTransform.Linear, WaveformDisplayTransform.Db60, WaveformDisplayTransform.Db96];
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
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ResetVideoTimingOffsetCommand { get; }
    public ICommand StepAudioPreviousCommand { get; }
    public ICommand StepAudioNextCommand { get; }
    public ICommand ToggleHoldCommand { get; }

    public MainViewModel()
    {
        _settings = _settingsService.Load(); _transientDetector.Sensitivity = _settings.AudioSensitivity; _backends = [new WindowsCaptureBackend(_log), new SyntheticCaptureBackend()];
        foreach (var theme in ThemeManager.Themes) Themes.Add(theme);
        _selectedTheme = Themes.First(theme => theme.Name == ThemeManager.Normalize(_settings.Theme)); ThemeManager.Apply(_selectedTheme.Name);
        _selectedAudioDisplayStyle = WaveformDisplayTransform.NormalizeStyle(_settings.AudioDisplayStyle);
        _selectedAudioDisplayAmplitude = WaveformDisplayTransform.NormalizeAmplitude(_settings.AudioDisplayAmplitude);
        RefreshCommand = new AsyncRelayCommand(RefreshDevicesAsync);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync);
        ManualClapCommand = new RelayCommand(_ => BeginManualClap());
        ResumeLiveCommand = new RelayCommand(_ => ResumeLive());
        StepPreviousCommand = new RelayCommand(_ => Step(-1));
        StepNextCommand = new RelayCommand(_ => Step(1));
        MarkVisualCommand = new RelayCommand(_ => MarkVisual());
        JumpAutoCommand = new RelayCommand(_ => JumpToAuto());
        CoffeeCommand = new RelayCommand(_ => OpenCoffee(), _ => !string.IsNullOrWhiteSpace(AppConstants.BuyMeACoffeeUrl));
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsPanelExpanded = !IsSettingsPanelExpanded);
        ResetVideoTimingOffsetCommand = new RelayCommand(_ => ResetVideoTimingOffset());
        StepAudioPreviousCommand = new RelayCommand(_ => StepAudio(-1));
        StepAudioNextCommand = new RelayCommand(_ => StepAudio(1));
        ToggleHoldCommand = new RelayCommand(_ => IsHold = !IsHold, _ => IsArmEnabled);
        _memoryTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => UpdateMemory(), Application.Current.Dispatcher);
    }

    public CaptureDeviceDescriptor? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!Set(ref _selectedDevice, value) || value is null) return;
            _settings.LastDeviceId = value.Id; _settings.LastDeviceName = value.FriendlyName;
            _reconstructInterlacedFields = _settings.ReconstructFieldsByDevice.GetValueOrDefault(value.Id);
            _observedSignal = null; RefreshInterpretationDisplay();
            SaveSettings(immediate: true);
            if (!_suppressDeviceRefresh) _ = RefreshCaptureFormatsAsync(value);
        }
    }
    public ResolutionOption? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (!Set(ref _selectedResolution, value) || _suppressInterpretationChanges) return;
            RebuildInterpretationOptions(); CommitInterpretationSelection();
        }
    }
    public InterpretationFormatOption? SelectedInterpretationFormat
    {
        get => _selectedInterpretationFormat;
        set
        {
            if (!Set(ref _selectedInterpretationFormat, value) || _suppressInterpretationChanges) return;
            var hideReconstructionChoices = _reconstructInterlacedFields && value?.ReconstructFields != true;
            _reconstructInterlacedFields = value?.ReconstructFields == true; Changed(nameof(ReconstructInterlacedFields)); Changed(nameof(FieldReconstructionHelpText));
            if (hideReconstructionChoices) RebuildInterpretationOptions();
            RebuildPixelFormats(); CommitInterpretationSelection();
        }
    }
    public PixelFormatOption? SelectedPixelFormat
    {
        get => _selectedPixelFormat;
        set
        {
            if (!Set(ref _selectedPixelFormat, value) || _suppressInterpretationChanges) return;
            CommitInterpretationSelection();
        }
    }
    public bool ReconstructInterlacedFields
    {
        get => _reconstructInterlacedFields;
        set
        {
            if (!Set(ref _reconstructInterlacedFields, value) || _suppressInterpretationChanges) return;
            RebuildInterpretationOptions(); CommitInterpretationSelection(); Changed(nameof(FieldReconstructionHelpText));
        }
    }
    public ProcessingResolutionOption? SelectedDetectionResolution
    {
        get => _selectedDetectionResolution;
        set
        {
            if (!Set(ref _selectedDetectionResolution, value) || value is null || _suppressInterpretationChanges) return;
            _settings.DetectionWidth = value.Width; _settings.DetectionHeight = value.Height; SaveSettings(); RefreshPerformanceDisplay(); _ = ReconnectAsync();
        }
    }
    public ProcessingResolutionOption? SelectedReviewResolution
    {
        get => _selectedReviewResolution;
        set
        {
            if (!Set(ref _selectedReviewResolution, value) || value is null || _suppressInterpretationChanges) return;
            _settings.ReviewWidth = value.Width; _settings.ReviewHeight = value.Height; SaveSettings(); RefreshPerformanceDisplay(); _ = ReconnectAsync();
        }
    }
    public ThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Set(ref _selectedTheme, value) || value is null) return;
            _settings.Theme = value.Name; ThemeManager.Apply(value.Name); SaveSettings();
        }
    }
    public string SelectedAudioDisplayStyle
    {
        get => _selectedAudioDisplayStyle;
        set
        {
            if (!AudioDisplayStyles.Contains(value) || !Set(ref _selectedAudioDisplayStyle, value)) return;
            _settings.AudioDisplayStyle = value; SaveSettings();
        }
    }
    public string SelectedAudioDisplayAmplitude
    {
        get => _selectedAudioDisplayAmplitude;
        set
        {
            if (!AudioDisplayAmplitudes.Contains(value) || !Set(ref _selectedAudioDisplayAmplitude, value)) return;
            _settings.AudioDisplayAmplitude = value; SaveSettings();
        }
    }
    public bool IsSettingsPanelExpanded
    {
        get => _settings.SettingsPanelExpanded;
        set
        {
            if (_settings.SettingsPanelExpanded == value) return;
            _settings.SettingsPanelExpanded = value; Changed(); Changed(nameof(SettingsColumnWidth)); Changed(nameof(SettingsPanelToggleText)); SaveSettings();
        }
    }
    public GridLength SettingsColumnWidth => new(IsSettingsPanelExpanded ? 370 : 54);
    public string SettingsPanelToggleText => IsSettingsPanelExpanded ? "◀  Hide settings" : "▶";
    public double VideoTimingOffsetMilliseconds
    {
        get => EffectiveVideoTimingOffset.Milliseconds;
        set
        {
            if (!double.IsFinite(value) || CurrentTimingProfileKey is not { } key) return;
            _settings.VideoTimingOffsetOverridesMilliseconds[key] = Math.Clamp(value, -5000, 5000);
            ApplyTimingOffsetChange(); SaveSettings();
        }
    }
    public bool IsVideoTimingOffsetManual => EffectiveVideoTimingOffset.IsManual;
    public string VideoTimingOffsetModeText => IsVideoTimingOffsetManual
        ? $"Manual profile override: {VideoTimingOffsetMilliseconds:+0.000;-0.000;0.000} ms\nApplied as {-VideoTimingOffsetMilliseconds:+0.000;-0.000;0.000} ms to visual timing."
        : $"Automatic reconstruction compensation: {VideoTimingOffsetMilliseconds:+0.000;-0.000;0.000} ms\n{(CurrentFieldReconstruction is null ? "No reconstructed-field delivery compensation." : "Capture card assumed to deliver the woven frame after both fields arrive.")}\nApplied as {-VideoTimingOffsetMilliseconds:+0.000;-0.000;0.000} ms to visual timing.";
    public bool AutoDetect { get => _settings.AutoDetect; set { if (_settings.AutoDetect != value) { _settings.AutoDetect = value; if (!value) IsHold = false; Changed(); Changed(nameof(ArmStateText)); Changed(nameof(IsArmEnabled)); CommandManager.InvalidateRequerySuggested(); SaveSettings(); } } }
    public bool AutoSpike { get => _settings.AutoSpike; set { if (_settings.AutoSpike != value) { _settings.AutoSpike = value; if (!value) IsHold = false; Changed(); Changed(nameof(ArmStateText)); Changed(nameof(IsArmEnabled)); CommandManager.InvalidateRequerySuggested(); SaveSettings(); } } }
    public bool AutoVisual { get => _settings.AutoVisual; set { if (_settings.AutoVisual != value) { _settings.AutoVisual = value; Changed(); SaveSettings(); } } }
    public int VisualSensitivity { get => Math.Clamp(_settings.VisualSensitivity, 0, 100); set { var clamped = Math.Clamp(value, 0, 100); if (_settings.VisualSensitivity != clamped) { _settings.VisualSensitivity = clamped; Changed(); SaveSettings(); } } }
    public int AudioSensitivity { get => Math.Clamp(_settings.AudioSensitivity, 0, 100); set { var clamped = Math.Clamp(value, 0, 100); if (_settings.AudioSensitivity != clamped) { _settings.AudioSensitivity = clamped; _transientDetector.Sensitivity = clamped; Changed(); SaveSettings(); } } }
    public double RollingBufferSeconds { get => _settings.RollingBufferSeconds; set { var v = Math.Clamp(value, 1, 30); if (_settings.RollingBufferSeconds != v) { _settings.RollingBufferSeconds = v; ResizeBuffers(); Changed(); RefreshPerformanceDisplay(); SaveSettings(); } } }
    public double WorkWindowMilliseconds { get => _settings.WorkWindowMilliseconds; set { var v = Math.Clamp(value, 50, 2000); if (_settings.WorkWindowMilliseconds != v) { _settings.WorkWindowMilliseconds = v; Changed(); Changed(nameof(HalfWindow)); SaveSettings(); } } }
    public double HalfWindow => WorkWindowMilliseconds;
    public ImageSource? VideoImage { get => _videoImage; private set => Set(ref _videoImage, value); }
    public ImageSource? AutoThumbnail { get => _autoThumbnail; private set => Set(ref _autoThumbnail, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string FormatText { get => _formatText; private set => Set(ref _formatText, value); }
    public string DetectedCaptureText => _capture is null ? "Not negotiated" : NativeFormatDisplay(_capture.CurrentFormat);
    public string InterpretationText => SelectedResolution is null || SelectedInterpretationFormat is null || SelectedPixelFormat is null ? "Not configured" : $"{SelectedResolution} · {SelectedInterpretationFormat} · {SelectedPixelFormat}";
    public string FieldReconstructionHelpText => CurrentFieldReconstruction is not null
        ? "Reconstructed field timing: selected field order. Capture timestamp is assumed to represent the second field / completed pair."
        : ReconstructInterlacedFields ? "Choose a valid reconstructed Format option, or keep a normal native interpretation." : "Use when a progressive capture frame contains two woven fields from an interlaced source.";
    public string VisualReviewResolutionText => $"Visual review resolution: {1000d / Math.Max(1, ExpectedReviewTemporalRate):0.###} ms{(CurrentFieldReconstruction is null ? "" : " · reconstructed fields")}";
    public string ReviewTimingText => _timelineAnalysis is null ? "REVIEW TIMING · live" : $"REVIEW TIMING · {_timelineAnalysis.Primary.ObservedRate:0.000} {(CurrentFieldReconstruction is null ? "frames" : "fields")}/sec · {_timelineAnalysis.Primary.MedianIntervalMilliseconds:0.000} ms{(CurrentFieldReconstruction is null ? "" : " · reconstructed")}";
    public string CaptureTransportText => _timelineAnalysis is null ? $"CAPTURE TRANSPORT · {ExpectedCaptureTransportRate:0.000} samples/sec" : $"CAPTURE TRANSPORT · {_timelineAnalysis.CaptureTransport.ObservedRate:0.000} samples/sec · {_timelineAnalysis.CaptureTransport.MedianIntervalMilliseconds:0.000} ms";
    public string TimestampPhaseText => CurrentFieldReconstruction is null ? "TIMESTAMP PHASE · direct capture" : "TIMESTAMP PHASE · assumed captured timestamp = second field";
    public string BufferEstimateText
    {
        get
        {
            var detection = SelectedDetectionResolution ?? new(_settings.DetectionWidth, _settings.DetectionHeight);
            var review = SelectedReviewResolution ?? new(_settings.ReviewWidth, _settings.ReviewHeight);
            var bytes = EstimatedImageBufferBytes;
            var size = bytes >= 1024d * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.00} GB" : $"{bytes / 1024d / 1024:0} MB";
            return $"Detection: {detection.Width}×{detection.Height}\nReview: {review.Width}×{review.Height}\nTemporal rate: {ExpectedReviewTemporalRate:0.000}/s · Buffer: {RollingBufferSeconds:0.0} s\nImage-buffer estimate: ~{size}";
        }
    }
    public string BufferEstimateWarning => EstimatedImageBufferBytes >= 1024L * 1024 * 1024 ? "VERY HIGH MEMORY REQUEST · memory grows as frames enter the buffer" : EstimatedImageBufferBytes >= 512L * 1024 * 1024 ? "HIGH MEMORY REQUEST · memory grows as frames enter the buffer" : "Memory grows as frames enter the rolling buffer.";
    public bool IsBufferEstimateVeryHigh => EstimatedImageBufferBytes >= 1024L * 1024 * 1024;
    public bool IsBufferEstimateHigh => EstimatedImageBufferBytes >= 512L * 1024 * 1024;
    public string ContentWarningText
    {
        get
        {
            var content = _timelineAnalysis?.Content;
            if (content?.PairedRepeatDetected == true) return PairedRepeatWarning(_timelineAnalysis!.Primary.ObservedRate, content.EstimatedUniqueImageRate);
            return _observedAnalysis?.PairedRepeatDetected == true ? PairedRepeatWarning(_observedAnalysis.ObservedFrameRate, _observedAnalysis.EstimatedUniqueImageRate) : "";
        }
    }
    public string TimingText { get => _timingText; private set => Set(ref _timingText, value); }
    public string MemoryText { get => _memoryText; private set => Set(ref _memoryText, value); }
    public double SystemFraction { get => _systemFraction; private set => Set(ref _systemFraction, value); }
    public double OtherSystemFraction { get => _otherSystemFraction; private set => Set(ref _otherSystemFraction, value); }
    public double AppFraction { get => _appFraction; private set => Set(ref _appFraction, value); }
    public double AvailableFraction { get => _availableFraction; private set => Set(ref _availableFraction, value); }
    public IReadOnlyList<float> Waveform { get => _waveform; private set => Set(ref _waveform, value); }
    public IReadOnlyList<double> ReviewFrameTicks { get => _reviewFrameTicks; private set => Set(ref _reviewFrameTicks, value); }
    public double AudioMarkerMs => _review?.AudioOffsetMs ?? 0;
    public double? AutoVisualMs => _review?.AutoOffsetMs;
    public double? VisualMarkerMs => _review?.ManualVisualOffsetMs;
    public double PlayheadMs => _review?.PlayheadOffsetMs ?? 0;
    public double PlayheadMeasurementMs => _review is null ? 0 : VideoTimingCompensation.Calculate(_review.AudioMark, _review.Playhead, _review.VideoTimingOffset).SignedMilliseconds;
    public SyncResult? CurrentResult => _review?.CurrentResult;
    public SyncResult? CurrentRawResult => _review?.CurrentRawResult;
    public string ResultModeText => _review?.ResultMode switch { ReviewResultMode.Auto => "AUTO RESULT", ReviewResultMode.ManualPreview => "MANUAL PREVIEW", ReviewResultMode.ManualResult => "MANUAL RESULT", _ => "" };
    public string ResultText => _timelineAnalysis is { TimingValid: false } ? "TIMELINE TIMING INVALID" : CurrentResult?.Wording ?? "WAITING FOR CLAP";
    public string ResultSummaryText => string.IsNullOrEmpty(ResultModeText) ? ResultText : $"{ResultModeText}\n{ResultText}";
    public double SyncValue => _timelineAnalysis is not { TimingValid: false } && CurrentResult is { TimingComparable: true } result ? result.SignedMilliseconds : 0;
    public string TimelineIntegrityText => _timelineAnalysis?.Status ?? "TIMELINE · LIVE";
    public string ConfidenceText => _review?.AutoCandidate is not { } candidate ? "—" : $"{candidate.Confidence:P0}";
    public string ReviewPosition
    {
        get
        {
            if (!_isReview || _reviewFrames.Count == 0 || _review is null) return "LIVE";
            var measurement = VideoTimingCompensation.Calculate(_review.AudioMark, _review.Playhead, _review.VideoTimingOffset);
            return $"{_playheadIndex + 1} / {_reviewFrames.Count} · {FieldLabel(_reviewFrames[_playheadIndex])} · {(measurement.TimingComparable ? $"{measurement.SignedMilliseconds:+0.0;-0.0;0} ms" : "TIMING NOT CORRELATED")}";
        }
    }
    public string RawMeasurementText => CurrentRawResult is { TimingComparable: true } raw ? $"{raw.SignedMilliseconds:+0.0;-0.0;0.0} ms" : "—";
    public string TimingCorrectionText => $"{-EffectiveVideoTimingOffset.Milliseconds:+0.0;-0.0;0.0} ms · {(EffectiveVideoTimingOffset.IsManual ? "manual profile override" : CurrentFieldReconstruction is null ? "automatic · none" : "automatic reconstructed-field compensation")}";
    // Hold is session-only and must never overwrite the capture readiness state.
    public bool IsHold { get => _isHold; set { if (Set(ref _isHold, value)) Changed(nameof(ArmStateText)); } }
    public bool IsArmEnabled => AutoDetect && AutoSpike;
    public string ArmStateText => !AutoDetect ? "AUTO OFF" : !AutoSpike ? "TRIGGER OFF" : IsHold ? "HOLD" : "ARMED";
    public bool IsInReview => _isReview;
    public string VersionText => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    public string BuildText => Services.BuildIdentity.Current.BuildId;
    public string BuildChannelText => $"{Services.BuildIdentity.Current.Channel} build";
    public string BuildCommitText => $"Commit {Services.BuildIdentity.Current.ShortCommit}";
    public string AboutTimingDiagnosticsText => $"Detected capture: {DetectedCaptureText}\nInterpreted format: {InterpretationText}\nReview temporal rate: {ExpectedReviewTemporalRate:0.000}/s\nNative transport rate: {ExpectedCaptureTransportRate:0.000}/s\nTiming source: {TimingText}\n{TimestampPhaseText}\nVideo compensation: {VideoTimingOffsetModeText}\nRaw measurement: {RawMeasurementText}\nCorrected measurement: {(CurrentResult is { TimingComparable: true } result ? $"{result.SignedMilliseconds:+0.0;-0.0;0.0} ms" : "—")}";
    public string FieldReconstructionAboutText => CurrentFieldReconstruction is { } reconstruction
        ? $"One woven progressive frame contains two fields. Kairix extracts {reconstruction.FieldOrder.ToString().Replace("First", " first").ToLowerInvariant()} fields before downscaling. Field interval: {reconstruction.FieldIntervalTicks100ns / 10_000d:0.0000} ms. The capture timestamp is treated as the completed pair / second field; automatic compensation is one field interval."
        : "Field reconstruction is off. Native progressive/interlaced temporal positions use direct capture timing and no automatic reconstructed-field compensation.";
    public string LastEventDiagnosticsText { get => _lastEventDiagnosticsText; private set => Set(ref _lastEventDiagnosticsText, value); }
    public string LogPath => _log.Path;

    public async Task InitializeAsync()
    {
        await RefreshDevicesAsync();
        if (Volatile.Read(ref _disposing) != 0) return;
        UpdateMemory(); _memoryTimer.Start(); await ReconnectAsync();
    }
    private async Task RefreshDevicesAsync()
    {
        var selected = SelectedDevice?.Id; var found = new List<CaptureDeviceDescriptor>();
        foreach (var backend in _backends)
        {
            try { found.AddRange(await backend.EnumerateDevicesAsync(_lifetimeCts.Token)); }
            catch (OperationCanceledException) when (Volatile.Read(ref _disposing) != 0) { return; }
            catch (Exception ex) { _log.Write("device.enumeration", $"backend={backend.Id} failed: {ex.Message}"); }
        }
        if (Volatile.Read(ref _disposing) != 0) return;
        var ranked = _deviceRanker.Rank(found, _settings.LastDeviceId, _settings.LastDeviceName); Devices.Clear(); foreach (var device in ranked) { Devices.Add(device); _log.Write("device.ranking", $"rank={Devices.Count} name='{device.FriendlyName}' kind={device.Kind} available={device.IsAvailable} container='{device.ContainerId ?? "unavailable"}'"); }
        _suppressDeviceRefresh = true;
        try { SelectedDevice = Devices.FirstOrDefault(d => d.Id == selected) ?? _deviceRanker.SelectBest(found, _settings.LastDeviceId, _settings.LastDeviceName) ?? Devices.FirstOrDefault(); }
        finally { _suppressDeviceRefresh = false; }
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
            try { options.AddRange(await provider.EnumerateFormatsAsync(device, _lifetimeCts.Token)); }
            catch (OperationCanceledException) when (Volatile.Read(ref _disposing) != 0) { return; }
            catch (Exception ex) { _log.Write("capture.format-list", $"device='{device.FriendlyName}' failed: {ex.Message}"); }
        }
        if (request != Volatile.Read(ref _formatRequest) || SelectedDevice?.Id != device.Id) return;
        var saved = _settings.NativeFormatByDevice.TryGetValue(device.Id, out var modeId) ? modeId : null;
        var selected = options.FirstOrDefault(option => string.Equals(option.Id, saved, StringComparison.Ordinal)) ?? CaptureFormatOption.Auto;
        if (selected == CaptureFormatOption.Auto && (!string.IsNullOrWhiteSpace(saved) || _reconstructInterlacedFields))
        {
            _settings.NativeFormatByDevice.Remove(device.Id);
            _settings.ReconstructFieldsByDevice.Remove(device.Id);
            _settings.ReconstructionFieldOrderByDevice.Remove(device.Id);
            _reconstructInterlacedFields = false;
            Changed(nameof(ReconstructInterlacedFields));
            SaveSettings(immediate: true);
            _log.Write("capture.format-selection", $"device='{device.FriendlyName}' saved interpretation is unavailable; falling back to Auto");
        }
        _allCaptureFormats = options;
        _selectedCaptureFormat = selected;
        RebuildInterpretationOptions(selected.Format);
        RefreshInterpretationDisplay();
    }

    private void RebuildInterpretationOptions(CaptureFormat? preferred = null)
    {
        _suppressInterpretationChanges = true;
        try
        {
            var modes = NativeModes();
            var priorResolution = preferred is null ? SelectedResolution : new ResolutionOption(preferred.Width, preferred.Height);
            Resolutions.Clear();
            foreach (var resolution in modes.Select(mode => new ResolutionOption(mode.Format!.Width, mode.Format.Height)).Distinct().OrderByDescending(value => value.Width * value.Height)) Resolutions.Add(resolution);
            SelectedResolution = Resolutions.FirstOrDefault(value => value == priorResolution) ?? Resolutions.FirstOrDefault();
            RebuildProcessingResolutionOptions();

            var priorFormat = SelectedInterpretationFormat;
            InterpretationFormats.Clear();
            if (SelectedResolution is not null)
            {
                var rates = modes.Where(mode => mode.Format!.Width == SelectedResolution.Width && mode.Format.Height == SelectedResolution.Height)
                    .Select(mode => mode.Format!).DistinctBy(format => (format.FrameRate, format.ScanMode, format.InterlaceLayout)).OrderByDescending(format => format.TemporalRate);
                foreach (var format in rates)
                {
                    InterpretationFormats.Add(new(format.FrameRate, format.ScanMode, TransportInterlaceLayout: format.InterlaceLayout));
                    if (ReconstructInterlacedFields && format.ScanMode == ScanMode.Progressive && ValidReconstructionRate(format.FrameRate))
                    {
                        var fieldRate = Rational.From(format.FrameRate.Numerator * 2, format.FrameRate.Denominator);
                        InterpretationFormats.Add(new(format.FrameRate, format.ScanMode, true, fieldRate, FieldOrder.TopFirst, format.InterlaceLayout));
                        InterpretationFormats.Add(new(format.FrameRate, format.ScanMode, true, fieldRate, FieldOrder.BottomFirst, format.InterlaceLayout));
                    }
                }
            }
            var savedOrder = SelectedDevice is { } device && _settings.ReconstructionFieldOrderByDevice.GetValueOrDefault(device.Id) == "bottom" ? FieldOrder.BottomFirst : FieldOrder.TopFirst;
            SelectedInterpretationFormat = InterpretationFormats.FirstOrDefault(value => preferred is not null && value.TransportRate == preferred.FrameRate && value.TransportScanMode == preferred.ScanMode && value.TransportInterlaceLayout == preferred.InterlaceLayout && value.ReconstructFields == ReconstructInterlacedFields && (!value.ReconstructFields || value.FieldOrder == savedOrder))
                ?? InterpretationFormats.FirstOrDefault(value => priorFormat is not null && value.TransportRate == priorFormat.TransportRate && value.TransportScanMode == priorFormat.TransportScanMode && value.TransportInterlaceLayout == priorFormat.TransportInterlaceLayout && value.ReconstructFields == ReconstructInterlacedFields && (!value.ReconstructFields || value.FieldOrder == savedOrder))
                ?? InterpretationFormats.FirstOrDefault(value => !value.ReconstructFields || value.FieldOrder == savedOrder)
                ?? InterpretationFormats.FirstOrDefault();
            if (_reconstructInterlacedFields && SelectedInterpretationFormat?.ReconstructFields != true) { _reconstructInterlacedFields = false; Changed(nameof(ReconstructInterlacedFields)); }
            RebuildPixelFormats(preferred?.PixelFormat);
        }
        finally { _suppressInterpretationChanges = false; }
    }

    private void RebuildPixelFormats(VideoPixelFormat? preferred = null)
    {
        var prior = preferred ?? SelectedPixelFormat?.Value;
        _suppressInterpretationChanges = true;
        try
        {
            PixelFormats.Clear();
            if (SelectedResolution is not null && SelectedInterpretationFormat is not null)
            {
                foreach (var pixel in NativeModes().Where(mode => MatchesSelection(mode.Format!)).Select(mode => mode.Format!.PixelFormat).Distinct()) PixelFormats.Add(new(pixel));
            }
            SelectedPixelFormat = PixelFormats.FirstOrDefault(value => value.Value == prior) ?? PixelFormats.FirstOrDefault();
        }
        finally { _suppressInterpretationChanges = false; }
    }

    private void RebuildProcessingResolutionOptions()
    {
        if (SelectedResolution is not { } source) return;
        var priorDetection = SelectedDetectionResolution;
        var priorReview = SelectedReviewResolution;
        DetectionResolutions.Clear(); foreach (var option in ScaleOptions(source.Width, source.Height, [180, 270, 360, 540, 720, 1080], 360)) DetectionResolutions.Add(option);
        ReviewResolutions.Clear(); foreach (var option in ScaleOptions(source.Width, source.Height, [90, 180, 270, 360], 90)) ReviewResolutions.Add(option);
        SelectedDetectionResolution = DetectionResolutions.FirstOrDefault(value => value.Width == _settings.DetectionWidth && value.Height == _settings.DetectionHeight)
            ?? DetectionResolutions.FirstOrDefault(value => value.Width == priorDetection?.Width && value.Height == priorDetection?.Height)
            ?? DetectionResolutions.MinBy(value => Math.Abs(value.Height - 360));
        SelectedReviewResolution = ReviewResolutions.FirstOrDefault(value => value.Width == _settings.ReviewWidth && value.Height == _settings.ReviewHeight)
            ?? ReviewResolutions.FirstOrDefault(value => value.Width == priorReview?.Width && value.Height == priorReview?.Height)
            ?? ReviewResolutions.MinBy(value => Math.Abs(value.Height - 90));
        if (SelectedDetectionResolution is { } detection) { _settings.DetectionWidth = detection.Width; _settings.DetectionHeight = detection.Height; }
        if (SelectedReviewResolution is { } review) { _settings.ReviewWidth = review.Width; _settings.ReviewHeight = review.Height; }
        RefreshPerformanceDisplay();
    }

    private static IReadOnlyList<ProcessingResolutionOption> ScaleOptions(int sourceWidth, int sourceHeight, IReadOnlyList<int> targetHeights, int balancedHeight)
    {
        var options = new List<ProcessingResolutionOption>();
        foreach (var requestedHeight in targetHeights)
        {
            var height = Math.Min(sourceHeight, requestedHeight);
            var width = height == sourceHeight ? sourceWidth : Math.Max(2, (int)Math.Round(sourceWidth * height / (double)sourceHeight / 2) * 2);
            width = Math.Min(sourceWidth, width);
            var description = requestedHeight == balancedHeight ? "Balanced" : null;
            var option = new ProcessingResolutionOption(width, height, description);
            if (!options.Any(existing => existing.Width == width && existing.Height == height)) options.Add(option);
        }
        return options.OrderBy(value => value.Width * value.Height).ToArray();
    }

    private IReadOnlyList<CaptureFormatOption> NativeModes() => _allCaptureFormats.Where(option => option.Format is not null).ToArray();
    private bool MatchesSelection(CaptureFormat format) => SelectedResolution is { } resolution && SelectedInterpretationFormat is { } interpretation &&
        format.Width == resolution.Width && format.Height == resolution.Height && format.FrameRate == interpretation.TransportRate && format.ScanMode == interpretation.TransportScanMode && format.InterlaceLayout == interpretation.TransportInterlaceLayout;
    private CaptureFormatOption? ResolveSelectedNativeMode() => SelectedPixelFormat is null ? null : NativeModes().FirstOrDefault(mode => MatchesSelection(mode.Format!) && mode.Format!.PixelFormat == SelectedPixelFormat.Value);

    private void CommitInterpretationSelection()
    {
        if (_suppressInterpretationChanges || SelectedDevice is null) return;
        var mode = ResolveSelectedNativeMode();
        if (mode is null) return;
        _selectedCaptureFormat = mode; RefreshInterpretationDisplay();
        _settings.NativeFormatByDevice[SelectedDevice.Id] = mode.Id;
        _settings.ReconstructFieldsByDevice[SelectedDevice.Id] = SelectedInterpretationFormat?.ReconstructFields == true;
        if (SelectedInterpretationFormat?.FieldOrder == FieldOrder.BottomFirst) _settings.ReconstructionFieldOrderByDevice[SelectedDevice.Id] = "bottom";
        else _settings.ReconstructionFieldOrderByDevice[SelectedDevice.Id] = "top";
        SaveSettings(immediate: true);
        _log.Write("capture.interpretation", $"device='{SelectedDevice.FriendlyName}' native='{mode.Id}' interpretation='{InterpretationText}' reconstruction={ReconstructInterlacedFields}");
        _ = ReconnectAsync();
    }

    public async Task ReconnectAsync()
    {
        if (Volatile.Read(ref _disposing) != 0) return;
        await _reconnectGate.WaitAsync();
        try
        {
        if (Volatile.Read(ref _disposing) != 0) return;
        _captureStatus = CaptureStatus.Starting; _captureStatusText = "RECONNECTING"; Status = _captureStatusText; ClearCurrentMediaState(); await StopCaptureAsync(); _video.Clear(); _audio.Clear(); _observedSignal = null; _observedAnalysis = null; _observedDeliveredRate = null; RefreshInterpretationDisplay();
        if (SelectedDevice is null) { Status = "NO CAPTURE DEVICE"; return; }
        var backend = _backends.FirstOrDefault(b => b.Id == SelectedDevice.BackendId); if (backend is null) { Status = "CAPTURE BACKEND NOT AVAILABLE"; return; }
        try
        {
            var strictFormat = !string.IsNullOrWhiteSpace(_selectedCaptureFormat?.Id);
            var detection = SelectedDetectionResolution ?? new(_settings.DetectionWidth, _settings.DetectionHeight);
            var review = SelectedReviewResolution ?? new(_settings.ReviewWidth, _settings.ReviewHeight);
            _capture = await backend.OpenAsync(SelectedDevice, new(detection.Width, detection.Height, PreferredNativeFormatId: _selectedCaptureFormat?.Id,
                PreferredPresentationWidth: review.Width, PreferredPresentationHeight: review.Height, RequirePreferredNativeFormat: strictFormat,
                FieldReconstruction: CurrentFieldReconstruction), _lifetimeCts.Token); _capture.VideoSampleReceived += OnVideoFrame; _capture.AudioSampleReceived += OnAudio; _capture.StatusChanged += OnCaptureStatus;
            await _capture.StartAsync(_lifetimeCts.Token);
            if (!strictFormat) ApplyDetectedCaptureDefaults(_capture.CurrentFormat);
            ResizeBuffers(); FormatText = BuildCaptureText(); TimingText = DescribeTiming(_capture.TimingQuality); RefreshInterpretationDisplay(); if (SelectedDevice.Kind == CaptureDeviceKind.Synthetic) Status = "READY TO CLAP"; _log.Write("capture", $"Opened {SelectedDevice.FriendlyName} using {backend.Id}; detected='{DetectedCaptureText}' interpretation='{InterpretationText}'");
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _disposing) != 0) { await StopCaptureAsync(); }
        catch (Exception ex) { _captureStatus = CaptureStatus.Failed; _captureStatusText = !string.IsNullOrWhiteSpace(_selectedCaptureFormat?.Id) ? "REQUESTED CAPTURE FORMAT NOT ACCEPTED" : "CAPTURE OPEN FAILED"; Status = _captureStatusText; TimingText = "TIMING UNAVAILABLE"; FormatText = !string.IsNullOrWhiteSpace(_selectedCaptureFormat?.Id) ? $"Requested: {_selectedCaptureFormat.Display}" : "Capture: Not negotiated"; _log.Write("capture", $"Open failed for {SelectedDevice.FriendlyName}: {ex}"); await StopCaptureAsync(); RefreshInterpretationDisplay(); }
        }
        finally { _reconnectGate.Release(); }
    }

    private void ClearCurrentMediaState()
    {
        _analysisGeneration.Next(); _analysisCts?.Cancel(); _isReview = false; Changed(nameof(IsInReview)); _review = null; _reviewFrames = []; _playheadIndex = 0; _lastAudioEndTicks = long.MinValue; _audioContinuityFaults = 0;
        Interlocked.Exchange(ref _pendingPreview, null); VideoImage = null; AutoThumbnail = null; Waveform = []; ReviewFrameTicks = []; _timelineAnalysis = null; FormatText = "Format not negotiated"; TimingText = "TIMING NOT AVAILABLE"; NotifyMarkers();
    }

    private void OnVideoFrame(object? sender, VideoFrame frame)
    {
        _video.Add(frame); ScheduleSignalAnalysis();
        if (!_isReview) ScheduleLatestPreview(frame);
    }

    private void ScheduleSignalAnalysis()
    {
        var now = DateTime.UtcNow.Ticks;
        if (now - Volatile.Read(ref _lastSignalAnalysisUtcTicks) < TimeSpan.FromSeconds(5).Ticks || Interlocked.CompareExchange(ref _signalAnalysisRunning, 1, 0) != 0) return;
        Volatile.Write(ref _lastSignalAnalysisUtcTicks, now);
        var frames = _video.Snapshot();
        if (frames.Count < 30) { Interlocked.Exchange(ref _signalAnalysisRunning, 0); return; }
        _ = Task.Run(() => _signalAnalyzer.Analyze(frames)).ContinueWith(task =>
        {
            Interlocked.Exchange(ref _signalAnalysisRunning, 0);
            if (!task.IsCompletedSuccessfully) return;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _observedSignal = task.Result.Signal.HasUsefulData ? task.Result.Signal : null;
                _observedAnalysis = task.Result;
                _observedDeliveredRate = task.Result.ObservedFrameRate > 0 ? task.Result.ObservedFrameRate : null;
                FormatText = BuildCaptureText();
                RefreshInterpretationDisplay();
                _log.Write("input-signal.analysis", $"frames={task.Result.FramesAnalyzed} duration={task.Result.DurationSeconds:0.00}s output={task.Result.ObservedFrameRate:0.###}Hz cadence={task.Result.Signal.EffectiveTemporalRate?.ToString("0.###") ?? "unknown"} nearIdentical={task.Result.NearIdenticalFraction:P1} sceneActivity={task.Result.SceneActivitySufficient} pairedRepeat={task.Result.PairedRepeatDetected} uniqueRate={task.Result.EstimatedUniqueImageRate?.ToString("0.###") ?? "unknown"} pattern='{task.Result.RepeatPattern}' interlace={task.Result.InterlaceEvidence:0.000} authority={task.Result.Signal.Authority}");
            });
        }, TaskScheduler.Default);
    }

    private void ScheduleLatestPreview(VideoFrame frame)
    {
        Interlocked.Exchange(ref _pendingPreview, frame);
        if (Interlocked.Exchange(ref _previewScheduled, 1) != 0) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var latest = Interlocked.Exchange(ref _pendingPreview, null); if (latest is not null && !_isReview) VideoImage = ToBitmap(latest);
            Interlocked.Exchange(ref _previewScheduled, 0);
            var newer = Interlocked.Exchange(ref _pendingPreview, null); if (newer is not null) ScheduleLatestPreview(newer);
            if (_capture is not null)
            {
                TimingText = DescribeTiming(_capture.TimingQuality);
                var format = BuildCaptureText();
                if (FormatText != format) FormatText = format;
            }
        }, DispatcherPriority.Render);
    }

    private void OnAudio(object? sender, AudioChunk chunk)
    {
        var priorEnd = Interlocked.Exchange(ref _lastAudioEndTicks, chunk.Timestamp.Ticks100ns + chunk.Duration.Ticks);
        if (priorEnd != long.MinValue && (chunk.Timestamp.Ticks100ns < priorEnd - TimeSpan.FromMilliseconds(1).Ticks || chunk.Timestamp.Ticks100ns > priorEnd + TimeSpan.FromMilliseconds(5).Ticks)) Interlocked.Increment(ref _audioContinuityFaults);
        _audio.Add(chunk); if (!ReviewTimeline.AcceptsAutomaticEvents(AutoDetect, IsHold) || !AutoSpike) return;
        foreach (var transient in _transientDetector.Process(chunk))
        {
            _log.Write($"Transient detected at {transient.Timestamp.Ticks100ns}; peak {transient.Peak:0.000}");
            Application.Current.Dispatcher.BeginInvoke(() => StartEvent(transient.Timestamp, audioFinalized: true));
        }
    }

    private void OnCaptureStatus(object? sender, CaptureStatusChangedEventArgs e)
    {
        _log.Write("capture.status", $"{e.Status}: {e.Message}");
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _captureStatus = e.Status;
            _captureStatusText = e.Status switch
            {
                CaptureStatus.Starting => e.Message.ToUpperInvariant(),
                CaptureStatus.Running => e.Message.ToUpperInvariant(),
                CaptureStatus.DeviceLost => "CAPTURE DEVICE LOST",
                CaptureStatus.Failed => "CAPTURE FAILED",
                CaptureStatus.Stopping => "STOPPING",
                CaptureStatus.Stopped => "CAPTURE STOPPED",
                _ => _captureStatusText
            };
            if (!_isReview) Status = _captureStatusText;
            if (_capture is not null) TimingText = DescribeTiming(_capture.TimingQuality);
        });
    }

    private async void StartEvent(MediaTimestamp audioMark, bool audioFinalized)
    {
        var generation = _analysisGeneration.Next();
        FinalizeCurrentEvent();
        var review = new EventReviewState(audioMark, audioMark, EffectiveVideoTimingOffset); _review = review; AutoThumbnail = null; _isReview = true; Changed(nameof(IsInReview)); Status = audioFinalized ? "REVIEW · ANALYSING" : "REVIEW · SELECT AUDIO MARK";
        await BuildEventSnapshotAsync(generation); if (!_analysisGeneration.IsCurrent(generation)) return; NotifyMarkers();
        _analysisCts?.Cancel(); _analysisCts = new();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(WorkWindowMilliseconds + Math.Max(0, review.VideoTimingOffset.Milliseconds) + 40), _analysisCts.Token);
            await BuildEventSnapshotAsync(generation); if (!_analysisGeneration.IsCurrent(generation)) return;
            if (AutoVisual && audioFinalized)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var candidate = await _visualDetector.DetectAsync(_reviewFrames, review.ExpectedRawVisualTimestamp, _analysisCts.Token, new(VisualSensitivity));
                stopwatch.Stop(); var analyzed = _reviewFrames.FirstOrDefault();
                _log.Write("visual.analysis", $"frames={_reviewFrames.Count} resolution={analyzed?.Width ?? 0}x{analyzed?.Height ?? 0} detail='{MotionVisualClapDetector.DetailDescription(analyzed?.Width ?? 0, analyzed?.Height ?? 0)}' sensitivity={VisualSensitivity} elapsedMs={stopwatch.Elapsed.TotalMilliseconds:0.0} candidate={(candidate is null ? "none" : candidate.Confidence.ToString("0.00"))}");
                if (candidate is not null && _analysisGeneration.IsCurrent(generation) && ReferenceEquals(_review, review))
                {
                    review.SetAutoCandidate(candidate); AutoThumbnail = ToBitmap(candidate.Frame);
                    if (review.ResultMode == ReviewResultMode.Auto) _playheadIndex = Math.Max(0, _reviewFrames.IndexOf(candidate.Frame));
                    var raw = SyncResult.Calculate(review.AudioMark, candidate.Timestamp);
                    var corrected = VideoTimingCompensation.Calculate(review.AudioMark, candidate.Timestamp, review.VideoTimingOffset);
                    var trace = candidate.Trace;
                    var detection = SelectedDetectionResolution ?? new(_settings.DetectionWidth, _settings.DetectionHeight);
                    var presentation = SelectedReviewResolution ?? new(_settings.ReviewWidth, _settings.ReviewHeight);
                    var processing = (_capture as ICapturePerformanceDiagnostics)?.GetProcessingDiagnostics();
                    LastEventDiagnosticsText = $"Detection {detection.Width}×{detection.Height} · review {presentation.Width}×{presentation.Height}\nAudio {review.AudioMark.Ticks100ns} · expected raw {review.ExpectedRawVisualTimestamp.Ticks100ns}\nChosen raw {candidate.Timestamp.Ticks100ns} · temporal {candidate.TemporalIndex} · native {candidate.Frame.NativeSampleIndex} · {FieldLabel(candidate.Frame)}\nRaw {raw.SignedMilliseconds:+0.000;-0.000;0.000} ms · compensation {review.VideoTimingOffset.Milliseconds:+0.000;-0.000;0.000} ms · corrected {corrected.SignedMilliseconds:+0.000;-0.000;0.000} ms\nScore {candidate.MotionScore:0.###} · confidence {candidate.Confidence:P0} · peak {trace?.ApproachPeakIndex.ToString() ?? "—"} · contact {trace?.FinalContactIndex.ToString() ?? "—"} · advanced {trace?.PositionsAdvanced.ToString() ?? "—"}\nAnalysis {stopwatch.Elapsed.TotalMilliseconds:0.0} ms · conversion avg/max {(processing is null ? "unavailable" : $"{processing.ConversionAverageMilliseconds:0.00}/{processing.ConversionMaximumMilliseconds:0.00} ms")} · timestamp source {candidate.Frame.TimingObservation?.PrimarySource.ToString() ?? candidate.Timestamp.Quality.ToString()}\nTimestamp faults: duplicate {_timelineAnalysis?.Primary.DuplicateCount ?? 0}, backwards {_timelineAnalysis?.Primary.BackwardCount ?? 0}, gaps {_timelineAnalysis?.Primary.LargeGapCount ?? 0}, audio continuity {Volatile.Read(ref _audioContinuityFaults)}";
                    _log.Write("visual.event", LastEventDiagnosticsText.Replace(Environment.NewLine, " | "));
                }
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
        if (_review is null) return; _review.MoveAudioMark(relativeMs, HalfWindow); NotifyMarkers();
        _log.Write($"Manual audio correction {relativeMs:+0.0;-0.0;0} ms");
    }
    public void PreviewAudioPoint(double relativeMs) { if (_review is null) return; _review.MoveAudioMark(relativeMs, HalfWindow); NotifyMarkers(); }
    public void StepAudio(double milliseconds)
    {
        if (_review is null) return;
        _review.MoveAudioMark(_review.AudioOffsetMs + milliseconds, HalfWindow); NotifyMarkers();
        _log.Write("review.audio-step", $"deltaMs={milliseconds:+0.0;-0.0;0.0} selectedMs={_review.AudioOffsetMs:+0.0;-0.0;0.0}");
    }
    public void MovePlayheadTo(double relativeMs)
    {
        if (_review is null || _reviewFrames.Count == 0) return;
        var rawRelativeMs = relativeMs + _review.VideoTimingOffset.Milliseconds;
        _playheadIndex = ReviewTimeline.NearestFrameIndex(_reviewFrames, _review.EventReference, rawRelativeMs); _review.MovePlayhead(_reviewFrames[_playheadIndex].Timestamp); ShowPlayhead(); NotifyMarkers();
    }
    private async Task BuildEventSnapshotAsync(long generation)
    {
        if (_review is null) return; var review = _review; var reference = review.EventReference; var half = TimeSpan.FromMilliseconds(WorkWindowMilliseconds); var videoSnapshot = _video.Snapshot(); var audioSnapshot = _audio.Snapshot();
        var declaredRate = ExpectedReviewTemporalRate;
        var built = await Task.Run(() =>
        {
            var timeline = ReviewTimelineIntegrity.Build(videoSnapshot, review.ExpectedRawVisualTimestamp, half, declaredRate, ExpectedCaptureTransportRate); var chunks = WorkWindowSelector.Around(audioSnapshot, a => a.Timestamp, reference, half + TimeSpan.FromMilliseconds(25));
            return (Timeline: timeline, Waveform: _waveformBuilder.Build(chunks, reference, half, 900));
        });
        if (!_analysisGeneration.IsCurrent(generation) || !ReferenceEquals(_review, review)) return;
        var priorPlayhead = review.Playhead; _reviewFrames = built.Timeline.Frames; _timelineAnalysis = built.Timeline.Analysis; _playheadIndex = _reviewFrames.Count == 0 ? 0 : FindNearest(_reviewFrames, priorPlayhead);
        if (_reviewFrames.Count > 0) review.SetInitialPlayhead(_reviewFrames[_playheadIndex].Timestamp);
        Waveform = built.Waveform; ReviewFrameTicks = _reviewFrames.Select(frame => VideoTimingCompensation.CorrectedOffsetMilliseconds(reference, frame.Timestamp, review.VideoTimingOffset)).ToArray();
        _log.Write("timeline.integrity", $"reviewDeclared={_timelineAnalysis.DeclaredTemporalRate:0.###} reviewObserved={_timelineAnalysis.Primary.ObservedRate:0.###} reviewMedianMs={_timelineAnalysis.Primary.MedianIntervalMilliseconds:0.###} transportDeclared={_timelineAnalysis.DeclaredTransportRate:0.###} transportObserved={_timelineAnalysis.CaptureTransport.ObservedRate:0.###} transportMedianMs={_timelineAnalysis.CaptureTransport.MedianIntervalMilliseconds:0.###} nativeSamples={_timelineAnalysis.NativeSamplesAnalyzed} rawFrames={built.Timeline.RawFrameCount} reviewFrames={_reviewFrames.Count} windowMs={half.TotalMilliseconds * 2:0.###} duplicateTimestamps={_timelineAnalysis.Primary.DuplicateCount} backwards={_timelineAnalysis.Primary.BackwardCount} gaps={_timelineAnalysis.Primary.LargeGapCount} nearIdentical={_timelineAnalysis.NearIdenticalConsecutiveImages} pairedRepeat={_timelineAnalysis.Content.PairedRepeatDetected} estimatedUniqueRate={_timelineAnalysis.Content.EstimatedUniqueImageRate?.ToString("0.###") ?? "unknown"} arrivalRate={_timelineAnalysis.Arrival.ObservedRate:0.###}");
        ShowPlayhead(); NotifyMarkers();
    }
    private static int FindNearest(IReadOnlyList<VideoFrame> frames, MediaTimestamp mark) => ReviewTimeline.NearestFrameIndex(frames, mark, 0);
    private void Step(int amount) { if (_review is null || _reviewFrames.Count == 0) return; _playheadIndex = Math.Clamp(_playheadIndex + amount, 0, _reviewFrames.Count - 1); _review.MovePlayhead(_reviewFrames[_playheadIndex].Timestamp); ShowPlayhead(); NotifyMarkers(); }
    public void StepTimeline(int amount) => Step(amount);
    public void StepCoarse(int direction) => Step(direction * 5);
    public void MarkAudioAtPlayhead() { if (_review is not null && _reviewFrames.Count > 0) SelectAudioPoint(PlayheadMs); }
    private void MarkVisual() { if (_review is null || _reviewFrames.Count == 0) return; _review.CommitManualVisual(); _log.Write($"Manual visual mark {_review.ManualVisualMark!.Value.Ticks100ns}"); NotifyMarkers(); }
    private void JumpToAuto() { if (_review?.AutoCandidate is not { } candidate || _reviewFrames.Count == 0) return; _playheadIndex = FindNearest(_reviewFrames, candidate.Timestamp); _review.ShowAutoCandidate(); ShowPlayhead(); NotifyMarkers(); }
    private void ShowPlayhead() { if (_reviewFrames.Count > 0) VideoImage = ToBitmap(_reviewFrames[Math.Clamp(_playheadIndex, 0, _reviewFrames.Count - 1)]); Changed(nameof(ReviewPosition)); Changed(nameof(PlayheadMs)); }
    private void ResumeLive() { _analysisGeneration.Next(); _analysisCts?.Cancel(); FinalizeCurrentEvent(); _isReview = false; Changed(nameof(IsInReview)); _review = null; _timelineAnalysis = null; Waveform = []; ReviewFrameTicks = []; AutoThumbnail = null; Status = AuthoritativeCaptureStatus(); NotifyMarkers(); }
    private void FinalizeCurrentEvent()
    {
        if (_review is null || _timelineAnalysis is { TimingValid: false } || !_review.TryFinalize(out var result) || result is null) return;
        _history.Add(new(DateTime.Now, result, _review.AutoCandidate?.Confidence, _review.CurrentRawResult, _review.VideoTimingOffset.Milliseconds, _review.VideoTimingOffset.Source)); RecentResults.Clear(); foreach (var item in _history.Items) RecentResults.Add(item);
    }
    private string AuthoritativeCaptureStatus() => _captureStatus switch { CaptureStatus.Running => _captureStatusText, CaptureStatus.Starting => _captureStatusText, CaptureStatus.DeviceLost => "CAPTURE DEVICE LOST", CaptureStatus.Failed => "CAPTURE FAILED", CaptureStatus.Stopping => "STOPPING", CaptureStatus.Stopped => "CAPTURE STOPPED", _ => "CAPTURE NOT READY" };
    private void NotifyMarkers() { Changed(nameof(AudioMarkerMs)); Changed(nameof(AutoVisualMs)); Changed(nameof(VisualMarkerMs)); Changed(nameof(CurrentResult)); Changed(nameof(CurrentRawResult)); Changed(nameof(ResultModeText)); Changed(nameof(ResultText)); Changed(nameof(ResultSummaryText)); Changed(nameof(SyncValue)); Changed(nameof(ConfidenceText)); Changed(nameof(PlayheadMs)); Changed(nameof(PlayheadMeasurementMs)); Changed(nameof(ReviewPosition)); Changed(nameof(RawMeasurementText)); Changed(nameof(TimingCorrectionText)); Changed(nameof(TimelineIntegrityText)); Changed(nameof(ReviewTimingText)); Changed(nameof(CaptureTransportText)); Changed(nameof(TimestampPhaseText)); Changed(nameof(ContentWarningText)); Changed(nameof(AboutTimingDiagnosticsText)); }
    private static BitmapSource ToBitmap(VideoFrame frame)
    {
        var bitmap = frame.HasPresentation
            ? BitmapSource.Create(frame.PresentationWidth, frame.PresentationHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, frame.PresentationBgra!, frame.EffectivePresentationStride)
            : BitmapSource.Create(frame.Width, frame.Height, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, frame.Luma, frame.Width);
        bitmap.Freeze(); return bitmap;
    }
    private void ResizeBuffers()
    {
        const double maximumRetainedVideoSamplesPerSecond = 60; // RAM safety bound; timestamps, never this capacity, define timeline positions.
        var negotiatedRate = _capture is null ? maximumRetainedVideoSamplesPerSecond : ExpectedReviewTemporalRate;
        var retainedVideoRate = Math.Clamp(negotiatedRate, 10, maximumRetainedVideoSamplesPerSecond);
        _video.Resize(Math.Max(50, (int)Math.Ceiling(RollingBufferSeconds * retainedVideoRate)));
        _audio.Resize(Math.Max(200, (int)Math.Ceiling(RollingBufferSeconds * 200))); // WASAPI packet bound; audio timing remains sample-derived.
        RefreshPerformanceDisplay();
    }
    private void UpdateMemory() { var m = _memoryService.Get(); if (m.TotalBytes == 0) return; SystemFraction = m.SystemUsedBytes / (double)m.TotalBytes; OtherSystemFraction = m.OtherSystemUsedBytes / (double)m.TotalBytes; AppFraction = m.ProcessUsedBytes / (double)m.TotalBytes; AvailableFraction = m.AvailableBytes / (double)m.TotalBytes; MemoryText = $"System: {Gb(m.SystemUsedBytes):0.0} GB   Kairix: {Gb(m.ProcessUsedBytes):0.00} GB   Available: {Gb(m.AvailableBytes):0.0} GB"; }
    private static double Gb(ulong bytes) => bytes / 1024d / 1024 / 1024;
    private void SaveSettings(bool immediate = false) { try { if (immediate) _settingsService.Save(_settings); else _settingsService.ScheduleSave(_settings); } catch (Exception ex) { _log.Write($"Settings save failed: {ex.Message}"); } }
    private static void OpenCoffee() { if (!string.IsNullOrWhiteSpace(AppConstants.BuyMeACoffeeUrl)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppConstants.BuyMeACoffeeUrl) { UseShellExecute = true }); }
    private static string DescribeTiming(TimingQuality quality) => quality switch { TimingQuality.DeviceHardware => "DEVICE/QPC TIMESTAMPS", TimingQuality.PlatformCaptureClock => "PLATFORM CAPTURE CLOCK", TimingQuality.ClockCorrelated => "CLOCKS CORRELATED", TimingQuality.StreamTimestamp => "STREAM TIMESTAMPS", TimingQuality.ArrivalFallback => "TIMING DEGRADED · ARRIVAL", _ => "TIMING DOMAINS UNRELATED" };
    private static string PairedRepeatWarning(double timestampRate, double? uniqueRate) => $"REPEATED FRAME PAIRS DETECTED · {timestampRate:0.##} timestamped fps · ≈{uniqueRate:0.##} unique images/sec · if this is an interlaced source, try half-rate progressive capture with field reconstruction";
    private static string FieldLabel(VideoFrame frame) => frame.TemporalImageKind switch { TemporalImageKind.TopField => "TOP FIELD", TemporalImageKind.BottomField => "BOTTOM FIELD", _ => "FRAME" };
    private string BuildCaptureText()
    {
        if (_capture is null) return "Capture: Not negotiated";
        var declared = _capture.CurrentFormat.FrameRate.Value;
        var observed = _observedDeliveredRate;
        var difference = observed.HasValue && declared > 0 ? Math.Abs(observed.Value - declared) / declared : 0;
        return difference >= .03 ? $"Capture: {_capture.CurrentFormat.Display} · observed {observed:0.###} fps" : $"Capture: {_capture.CurrentFormat.Display}";
    }
    private FieldReconstructionOptions? CurrentFieldReconstruction => ReconstructInterlacedFields && SelectedInterpretationFormat is { ReconstructFields: true, InterpretedFieldRate: { } fieldRate } interpretation
        ? new(interpretation.TransportRate, fieldRate, interpretation.FieldOrder)
        : null;
    private string? CurrentTimingProfileKey => SelectedDevice is null || string.IsNullOrWhiteSpace(_selectedCaptureFormat?.Id)
        ? null
        : VideoTimingProfileKey.Create(SelectedDevice.Id, _selectedCaptureFormat.Id, CurrentFieldReconstruction);
    private VideoTimingOffset EffectiveVideoTimingOffset
    {
        get
        {
            var manual = CurrentTimingProfileKey is { } key && _settings.VideoTimingOffsetOverridesMilliseconds.TryGetValue(key, out var value) ? value : (double?)null;
            return VideoTimingCompensation.Resolve(CurrentFieldReconstruction, manual);
        }
    }
    private void ResetVideoTimingOffset()
    {
        if (CurrentTimingProfileKey is { } key) _settings.VideoTimingOffsetOverridesMilliseconds.Remove(key);
        ApplyTimingOffsetChange(); SaveSettings();
    }
    private void ApplyTimingOffsetChange()
    {
        var offset = EffectiveVideoTimingOffset;
        if (_review is { } review)
        {
            review.SetVideoTimingOffset(offset);
            ReviewFrameTicks = _reviewFrames.Select(frame => VideoTimingCompensation.CorrectedOffsetMilliseconds(review.EventReference, frame.Timestamp, offset)).ToArray();
        }
        Changed(nameof(VideoTimingOffsetMilliseconds)); Changed(nameof(IsVideoTimingOffsetManual)); Changed(nameof(VideoTimingOffsetModeText)); Changed(nameof(FieldReconstructionAboutText)); NotifyMarkers();
    }
    private double ExpectedReviewTemporalRate
    {
        get
        {
            if (CurrentFieldReconstruction is { } reconstruction) return reconstruction.ReviewTemporalRate;
            var seconds = _capture?.CurrentFormat.TemporalImageDuration.TotalSeconds ?? 0;
            return seconds > 0 ? 1 / seconds : SelectedInterpretationFormat?.ReviewTemporalRate ?? 0;
        }
    }
    private double ExpectedCaptureTransportRate => _capture?.CurrentFormat.FrameRate.Value ?? SelectedInterpretationFormat?.TransportRate.Value ?? 0;
    private double EstimatedImageBufferBytes
    {
        get
        {
            var detection = SelectedDetectionResolution ?? new(_settings.DetectionWidth, _settings.DetectionHeight);
            var review = SelectedReviewResolution ?? new(_settings.ReviewWidth, _settings.ReviewHeight);
            return (detection.Width * (double)detection.Height + review.Width * (double)review.Height * 4) * Math.Max(0, ExpectedReviewTemporalRate) * RollingBufferSeconds;
        }
    }
    private static bool ValidReconstructionRate(Rational rate) => rate == Rational.From(25) || rate == Rational.From(30_000, 1_001);
    private static string NativeFormatDisplay(CaptureFormat format) => CaptureFormatFormatter.Format(format);
    private void ApplyDetectedCaptureDefaults(CaptureFormat format)
    {
        var matching = NativeModes().FirstOrDefault(option => option.Format == format || option.Format is { } candidate && candidate.Width == format.Width && candidate.Height == format.Height && candidate.FrameRate == format.FrameRate && candidate.ScanMode == format.ScanMode && candidate.InterlaceLayout == format.InterlaceLayout && candidate.PixelFormat == format.PixelFormat);
        if (matching is null)
        {
            matching = new CaptureFormatOption("", NativeFormatDisplay(format), format);
            _allCaptureFormats = [.. _allCaptureFormats, matching];
        }
        _selectedCaptureFormat = matching;
        RebuildInterpretationOptions(format);
    }
    private void RefreshInterpretationDisplay()
    {
        Changed(nameof(DetectedCaptureText)); Changed(nameof(InterpretationText)); Changed(nameof(FieldReconstructionHelpText)); Changed(nameof(VisualReviewResolutionText)); Changed(nameof(ReviewTimingText)); Changed(nameof(CaptureTransportText)); Changed(nameof(TimestampPhaseText)); Changed(nameof(ContentWarningText)); Changed(nameof(VideoTimingOffsetMilliseconds)); Changed(nameof(IsVideoTimingOffsetManual)); Changed(nameof(VideoTimingOffsetModeText)); Changed(nameof(TimingCorrectionText)); Changed(nameof(FieldReconstructionAboutText)); Changed(nameof(AboutTimingDiagnosticsText)); RefreshPerformanceDisplay();
    }
    private void RefreshPerformanceDisplay() { Changed(nameof(BufferEstimateText)); Changed(nameof(BufferEstimateWarning)); Changed(nameof(IsBufferEstimateHigh)); Changed(nameof(IsBufferEstimateVeryHigh)); }
    private async Task StopCaptureAsync() { if (_capture is null) return; _capture.VideoSampleReceived -= OnVideoFrame; _capture.AudioSampleReceived -= OnAudio; _capture.StatusChanged -= OnCaptureStatus; await _capture.DisposeAsync(); _capture = null; }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0) return;
        _log.Write("shutdown", "View model disposal started; canceling capture lifetime.");
        _lifetimeCts.Cancel(); _memoryTimer.Stop(); _analysisCts?.Cancel(); SaveSettings(immediate: true);
        await _reconnectGate.WaitAsync();
        try { await StopCaptureAsync(); _log.Write("shutdown", "Capture disposal completed."); }
        finally { _reconnectGate.Release(); _lifetimeCts.Dispose(); _settingsService.Dispose(); }
    }
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
