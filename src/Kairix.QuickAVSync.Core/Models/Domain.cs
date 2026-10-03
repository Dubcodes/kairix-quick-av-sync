namespace Kairix.QuickAVSync.Models;

public enum TimingQuality
{
    DeviceHardware = 5,
    PlatformCaptureClock = 4,
    ClockCorrelated = 3,
    StreamTimestamp = 2,
    ArrivalFallback = 1,
    Unrelated = 0
}

public enum ScanMode { Unknown, Progressive, Interlaced }
public enum FieldOrder { Unknown, TopFirst, BottomFirst }
public enum InterlaceLayout { Unknown, FullFrame, SingleField, Mixed }
public enum VideoPixelFormat { Luma8, Nv12, Yuy2, Uyvy, Bgra32, Bgr24, Unknown }
public enum AudioSampleFormat { Float32, SignedPcm16, Unknown }
public enum CaptureDeviceKind { Synthetic, ExternalCapture, IntegratedCamera, Unknown }
public enum CaptureStatus { Created, Starting, Running, Stopping, Stopped, DeviceLost, Failed }
public enum PairingConfidence { None, NameFallback, HardwareParent, ExactContainer }
public enum InputSignalProvenance { Unknown, OperatingSystem, DeviceStandardProperty, VendorApi, ObservedAnalysis, UserDeclared }
public enum SignalAuthority { Unknown, EstimatedLow, EstimatedMedium, EstimatedHigh, Authoritative }
public enum SignalLockStatus { Unknown, Unlocked, Locking, Locked }
public enum TemporalImageKind { ProgressiveFrame, TopField, BottomField }
public enum TimestampOrigin { DirectCapture, ReconstructedFirstField, CaptureTimestampAssumedSecondField }
public enum TimestampPhaseAssumption { CaptureTimestampRepresentsSecondField }
public enum VideoTimingOffsetSource { Automatic, Manual }

public readonly record struct Rational(int Numerator, int Denominator)
{
    public Rational Reduce()
    {
        if (Denominator == 0) throw new DivideByZeroException();
        var gcd = GreatestCommonDivisor(Math.Abs(Numerator), Math.Abs(Denominator));
        var sign = Denominator < 0 ? -1 : 1;
        return new(Numerator / gcd * sign, Denominator / gcd * sign);
    }
    public double Value => (double)Numerator / Denominator;
    public TimeSpan FrameDuration => TimeSpan.FromSeconds(1 / Value);
    public static Rational From(int numerator, int denominator = 1) => new Rational(numerator, denominator).Reduce();
    private static int GreatestCommonDivisor(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Max(1, a); }
    public override string ToString() => Denominator == 1 ? Numerator.ToString() : $"{Numerator}/{Denominator}";
}

public sealed record CaptureFormat(
    int Width,
    int Height,
    Rational FrameRate,
    ScanMode ScanMode,
    FieldOrder FieldOrder,
    int AudioSampleRate = 0,
    VideoPixelFormat PixelFormat = VideoPixelFormat.Luma8,
    InterlaceLayout InterlaceLayout = InterlaceLayout.Unknown)
{
    public double TemporalRate => CaptureFormatFormatter.TemporalRate(FrameRate, ScanMode, InterlaceLayout);
    public TimeSpan TemporalImageDuration => TimeSpan.FromSeconds(1 / TemporalRate);
    public string Display => CaptureFormatFormatter.Format(this, includePixelFormat: false, includeAudio: true);
}

public static class CaptureFormatFormatter
{
    public static double TemporalRate(Rational storedRate, ScanMode scanMode, InterlaceLayout layout) =>
        scanMode == ScanMode.Interlaced && layout == InterlaceLayout.FullFrame ? storedRate.Value * 2 : storedRate.Value;

    public static string FormatRate(Rational storedRate, ScanMode scanMode, InterlaceLayout layout = InterlaceLayout.Unknown)
    {
        if (layout == InterlaceLayout.Mixed) return $"{storedRate.Value:0.000} fps · mixed interlace";
        return scanMode switch
        {
            ScanMode.Progressive => $"{storedRate.Value:0.000}p",
            ScanMode.Interlaced when layout == InterlaceLayout.FullFrame => $"{storedRate.Value * 2:0.000}i",
            ScanMode.Interlaced when layout == InterlaceLayout.SingleField => $"{storedRate.Value:0.000}i",
            ScanMode.Interlaced => $"{storedRate.Value:0.000} fps · interlaced",
            _ => $"{storedRate.Value:0.000} fps · scan unknown"
        };
    }

    public static string Format(CaptureFormat format, bool includePixelFormat = true, bool includeAudio = false)
    {
        var text = $"{format.Width}×{format.Height} · {FormatRate(format.FrameRate, format.ScanMode, format.InterlaceLayout)}";
        if (includePixelFormat) text += $" · {new PixelFormatOption(format.PixelFormat)}";
        if (includeAudio && format.AudioSampleRate > 0) text += $" · audio {format.AudioSampleRate / 1000} kHz";
        return text;
    }
}

public readonly record struct MediaTimestamp(
    long Ticks100ns,
    TimingQuality Quality,
    string ClockDomain = "stream",
    long? RawValue = null)
{
    public TimeSpan Time => TimeSpan.FromTicks(Ticks100ns);
    public bool IsComparableTo(MediaTimestamp other) =>
        Quality != TimingQuality.Unrelated && other.Quality != TimingQuality.Unrelated &&
        string.Equals(ClockDomain, other.ClockDomain, StringComparison.Ordinal);
    public static MediaTimestamp FromTimeSpan(TimeSpan time, TimingQuality quality, string domain = "stream") => new(time.Ticks, quality, domain, time.Ticks);
}

// Arrays are safely owned by the sample and must not be mutated after publication.
// Luma drives analysis; the optional bounded BGRA presentation buffer is UI-only.
public sealed record VideoFrame(
    MediaTimestamp Timestamp,
    int Width,
    int Height,
    byte[] Luma,
    int TemporalIndex,
    TemporalImageKind TemporalImageKind = TemporalImageKind.ProgressiveFrame,
    TimestampOrigin TimestampOrigin = TimestampOrigin.DirectCapture,
    int Stride = 0,
    VideoPixelFormat PixelFormat = VideoPixelFormat.Luma8,
    byte[]? PresentationBgra = null,
    int PresentationWidth = 0,
    int PresentationHeight = 0,
    int PresentationStride = 0,
    VideoTimingObservation? TimingObservation = null,
    long NativeSampleIndex = -1)
{
    public bool IsField => TemporalImageKind is TemporalImageKind.TopField or TemporalImageKind.BottomField;
    public bool TopField => TemporalImageKind == TemporalImageKind.TopField;
    public int EffectiveStride => Stride > 0 ? Stride : Width;
    public bool HasPresentation => PresentationBgra is { Length: > 0 } && PresentationWidth > 0 && PresentationHeight > 0;
    public int EffectivePresentationStride => PresentationStride > 0 ? PresentationStride : PresentationWidth * 4;
}

public enum VideoPrimaryTimestampSource { Unknown, DeviceTimestamp, SampleTime, ReaderTimestamp, ArrivalFallback }

public sealed record VideoTimingObservation(
    VideoPrimaryTimestampSource PrimarySource,
    long ArrivalStopwatchTicks,
    long StopwatchFrequency,
    long? DeviceTimestampTicks100ns = null,
    long? SampleTimeTicks100ns = null,
    long? ReaderTimestampTicks100ns = null);

public sealed record CadenceStatistics(
    int Samples,
    int Intervals,
    double ObservedRate,
    double MedianIntervalMilliseconds,
    double MinimumIntervalMilliseconds,
    double MaximumIntervalMilliseconds,
    double JitterPercent,
    int DuplicateCount,
    int BackwardCount,
    int LargeGapCount);

public sealed record FrameTimingAnalysis(
    double DeclaredTemporalRate,
    double ExpectedIntervalMilliseconds,
    double DeclaredTransportRate,
    CadenceStatistics CaptureTransport,
    CadenceStatistics Primary,
    CadenceStatistics Arrival,
    CadenceStatistics DeviceTimestamp,
    CadenceStatistics SampleTime,
    CadenceStatistics ReaderTimestamp,
    int FramesAnalyzed,
    int NativeSamplesAnalyzed,
    FrameContentAnalysis Content,
    double DeclaredVsObservedErrorPercent,
    bool TimingValid,
    string Status)
{
    public int NearIdenticalConsecutiveImages => Content.NearIdenticalConsecutiveImages;
}

public sealed record FrameContentAnalysis(
    int Comparisons,
    int NearIdenticalConsecutiveImages,
    double NearIdenticalFraction,
    bool SceneActivitySufficient,
    bool PairedRepeatDetected,
    double? EstimatedUniqueImageRate,
    string Status);

public sealed record AudioChunk(
    MediaTimestamp Timestamp,
    float[] Samples,
    int SampleRate,
    int Channels,
    AudioSampleFormat SourceFormat = AudioSampleFormat.Float32)
{
    public int FrameCount => Channels <= 0 ? 0 : Samples.Length / Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);
}

public sealed record AudioTransient(MediaTimestamp Timestamp, float Peak, float NoiseFloor);
public sealed record VisualDetectionTrace(
    int ApproachPeakIndex,
    int FinalContactIndex,
    int PositionsAdvanced,
    double ApproachEvidence,
    double ContactEvidence,
    double PostContactEvidence);

public sealed record VisualCandidate(
    MediaTimestamp Timestamp,
    int TemporalIndex,
    double Confidence,
    double MotionScore,
    VideoFrame Frame,
    VisualDetectionTrace? Trace = null);
public sealed record VisualDetectionOptions(int Sensitivity = 50)
{
    public int ClampedSensitivity => Math.Clamp(Sensitivity, 0, 100);
    public double NormalizedSensitivity => ClampedSensitivity / 100d;
}

public sealed record InputSignalInfo(
    int? Width = null,
    int? Height = null,
    Rational? FrameRate = null,
    double? FieldRate = null,
    double? TemporalCadenceHz = null,
    ScanMode ScanMode = ScanMode.Unknown,
    FieldOrder FieldOrder = FieldOrder.Unknown,
    SignalLockStatus LockStatus = SignalLockStatus.Unknown,
    InputSignalProvenance Provenance = InputSignalProvenance.Unknown,
    SignalAuthority Authority = SignalAuthority.Unknown,
    string? ProviderName = null,
    string? DeviceIdentity = null)
{
    public static InputSignalInfo Unknown { get; } = new();
    public double? EffectiveTemporalRate => TemporalCadenceHz ?? (ScanMode == ScanMode.Interlaced ? FieldRate ?? FrameRate?.Value * 2 : FrameRate?.Value ?? FieldRate);
    public bool HasUsefulData => EffectiveTemporalRate is not null || Width is not null || Height is not null || ScanMode != ScanMode.Unknown || LockStatus != SignalLockStatus.Unknown;
}

public sealed record ObservedSignalAnalysis(
    InputSignalInfo Signal,
    double ObservedFrameRate,
    double MedianIntervalMilliseconds,
    double JitterPercent,
    double NearIdenticalFraction,
    string RepeatPattern,
    double InterlaceEvidence,
    int FramesAnalyzed,
    double DurationSeconds,
    bool SceneActivitySufficient = false,
    bool PairedRepeatDetected = false,
    double? EstimatedUniqueImageRate = null);

public sealed record InputSignalOption(string Id, string Display, InputSignalInfo? Signal = null)
{
    public static InputSignalOption Auto { get; } = new("", "Auto / Detect");
    public override string ToString() => Display;
}

public sealed record ResolutionOption(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}

public sealed record ProcessingResolutionOption(int Width, int Height, string? Description = null)
{
    public override string ToString() => $"{Width}×{Height}{(string.IsNullOrWhiteSpace(Description) ? "" : $" · {Description}")}";
}

public sealed record InterpretationFormatOption(
    Rational TransportRate,
    ScanMode TransportScanMode,
    bool ReconstructFields = false,
    Rational? InterpretedFieldRate = null,
    FieldOrder FieldOrder = FieldOrder.Unknown,
    InterlaceLayout TransportInterlaceLayout = InterlaceLayout.Unknown)
{
    public double ReviewTemporalRate => ReconstructFields ? InterpretedFieldRate?.Value ?? TransportRate.Value * 2 : CaptureFormatFormatter.TemporalRate(TransportRate, TransportScanMode, TransportInterlaceLayout);
    public string TransportDisplay => CaptureFormatFormatter.FormatRate(TransportRate, TransportScanMode, TransportInterlaceLayout);
    public override string ToString() => !ReconstructFields
        ? TransportDisplay
        : $"{TransportDisplay} → {InterpretedFieldRate?.Value ?? TransportRate.Value * 2:0.000}i · {(FieldOrder == FieldOrder.BottomFirst ? "Bottom first" : "Top first")}";
}

public sealed record PixelFormatOption(VideoPixelFormat Value)
{
    public override string ToString() => Value switch
    {
        VideoPixelFormat.Nv12 => "NV12",
        VideoPixelFormat.Yuy2 => "YUY2",
        VideoPixelFormat.Uyvy => "UYVY",
        VideoPixelFormat.Bgr24 => "RGB24",
        VideoPixelFormat.Bgra32 => "BGRA32",
        VideoPixelFormat.Luma8 => "Luma8",
        _ => "Unknown"
    };
}

public sealed record FieldReconstructionOptions(
    Rational CapturedProgressiveRate,
    Rational TargetFieldRate,
    FieldOrder FieldOrder,
    TimestampPhaseAssumption TimestampPhase = TimestampPhaseAssumption.CaptureTimestampRepresentsSecondField)
{
    public long FieldIntervalTicks100ns => Services.ReconstructedFieldTimestampModel.FieldIntervalTicks100ns(TargetFieldRate);
    public double ReviewTemporalRate => TargetFieldRate.Value;
}

public sealed record CaptureDeviceDescriptor(
    string Id,
    string FriendlyName,
    CaptureDeviceKind Kind,
    string BackendId,
    string? ContainerId = null,
    string? ParentId = null,
    bool IsAvailable = true,
    IReadOnlyDictionary<string, string>? Properties = null)
{
    public override string ToString() => FriendlyName;
}

public sealed record AudioEndpointDescriptor(
    string Id,
    string FriendlyName,
    string? ContainerId,
    string? ParentId = null,
    bool IsDefaultMicrophone = false,
    bool IsActive = true);

public sealed record DevicePairing(AudioEndpointDescriptor? Endpoint, PairingConfidence Confidence, string Reason)
{
    public bool IsCertain => Confidence is PairingConfidence.ExactContainer or PairingConfidence.HardwareParent;
}

public sealed record CaptureFormatOption(string Id, string Display, CaptureFormat? Format = null)
{
    public static CaptureFormatOption Auto { get; } = new("", "Auto — best native format");
    public override string ToString() => Display;
}

public sealed record CaptureOpenOptions(
    int PreferredAnalysisWidth = 640,
    int PreferredAnalysisHeight = 360,
    bool IncludeAudio = true,
    string? PreferredNativeFormatId = null,
    int PreferredPresentationWidth = 160,
    int PreferredPresentationHeight = 90,
    InputSignalInfo? PreferredSourceSignal = null,
    bool RequirePreferredNativeFormat = false,
    FieldReconstructionOptions? FieldReconstruction = null);
public sealed record CaptureStatusChangedEventArgs(CaptureStatus Status, string Message, Exception? Error = null);

public sealed record SyncResult(double SignedMilliseconds, string Wording, bool TimingComparable = true)
{
    public static SyncResult Calculate(MediaTimestamp audio, MediaTimestamp visual)
    {
        if (!audio.IsComparableTo(visual)) return new(double.NaN, "TIMING DOMAINS NOT CORRELATED", false);
        var delta = (visual.Ticks100ns - audio.Ticks100ns) / 10_000d;
        var rounded = Math.Round(Math.Abs(delta), 1);
        var words = Math.Abs(delta) < 0.05 ? "AUDIO AND VIDEO IN SYNC"
            : delta > 0 ? $"AUDIO LEADS VIDEO BY {rounded:0.#} ms"
            : $"AUDIO LAGS VIDEO BY {rounded:0.#} ms";
        return new(delta, words);
    }
}

public sealed record SessionResult(
    DateTime Time,
    SyncResult Result,
    double? Confidence,
    SyncResult? RawResult = null,
    double VideoTimingOffsetMilliseconds = 0,
    VideoTimingOffsetSource VideoTimingOffsetSource = VideoTimingOffsetSource.Automatic);

public sealed class AppSettings
{
    public string? LastDeviceId { get; set; }
    public string? LastDeviceName { get; set; }
    public bool AutoDetect { get; set; } = true;
    public bool AutoSpike { get; set; } = true;
    public bool AutoVisual { get; set; } = true;
    public int VisualSensitivity { get; set; } = 50;
    public double RollingBufferSeconds { get; set; } = 5;
    public double WorkWindowMilliseconds { get; set; } = 250;
    public int DetectionWidth { get; set; } = 640;
    public int DetectionHeight { get; set; } = 360;
    public int ReviewWidth { get; set; } = 160;
    public int ReviewHeight { get; set; } = 90;
    public string Theme { get; set; } = "Graphite";
    public string AudioDisplayStyle { get; set; } = "Mirrored";
    public bool SettingsPanelExpanded { get; set; } = true;
    public Dictionary<string, string> NativeFormatByDevice { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> ReconstructFieldsByDevice { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ReconstructionFieldOrderByDevice { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> VideoTimingOffsetOverridesMilliseconds { get; set; } = new(StringComparer.Ordinal);
}
