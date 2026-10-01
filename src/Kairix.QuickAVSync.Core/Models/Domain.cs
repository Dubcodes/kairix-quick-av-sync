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
    public TimeSpan TemporalImageDuration => ScanMode == ScanMode.Interlaced && InterlaceLayout == InterlaceLayout.FullFrame
        ? TimeSpan.FromTicks(FrameRate.FrameDuration.Ticks / 2) : FrameRate.FrameDuration;
    private string ScanDisplay => ScanMode switch
    {
        _ when InterlaceLayout == InterlaceLayout.Mixed => $"{FrameRate.Value:0.##} fps · mixed interlace",
        ScanMode.Progressive => $"{FrameRate.Value:0.##}p",
        ScanMode.Interlaced when InterlaceLayout == InterlaceLayout.FullFrame => $"{FrameRate.Value * 2:0.##}i",
        ScanMode.Interlaced when InterlaceLayout == InterlaceLayout.SingleField => $"{FrameRate.Value:0.##}i",
        ScanMode.Interlaced => $"{FrameRate.Value:0.##} fps · interlaced",
        _ => $"{FrameRate.Value:0.##} fps · scan unknown"
    };
    public string Display => $"{Width}×{Height} · {ScanDisplay}{(AudioSampleRate > 0 ? $" · audio {AudioSampleRate / 1000} kHz" : "")}";
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
    bool IsField = false,
    bool TopField = false,
    int Stride = 0,
    VideoPixelFormat PixelFormat = VideoPixelFormat.Luma8,
    byte[]? PresentationBgra = null,
    int PresentationWidth = 0,
    int PresentationHeight = 0,
    int PresentationStride = 0)
{
    public int EffectiveStride => Stride > 0 ? Stride : Width;
    public bool HasPresentation => PresentationBgra is { Length: > 0 } && PresentationWidth > 0 && PresentationHeight > 0;
    public int EffectivePresentationStride => PresentationStride > 0 ? PresentationStride : PresentationWidth * 4;
}

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
public sealed record VisualCandidate(MediaTimestamp Timestamp, int TemporalIndex, double Confidence, double MotionScore, VideoFrame Frame);
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
    double DuplicateFraction,
    string RepeatPattern,
    double InterlaceEvidence,
    int FramesAnalyzed,
    double DurationSeconds);

public sealed record InputSignalOption(string Id, string Display, InputSignalInfo? Signal = null)
{
    public static InputSignalOption Auto { get; } = new("", "Auto / Detect");
    public override string ToString() => Display;
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
    InputSignalInfo? PreferredSourceSignal = null);
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

public sealed record SessionResult(DateTime Time, SyncResult Result, double? Confidence);

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
    public Dictionary<string, string> NativeFormatByDevice { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> InputSignalByDevice { get; set; } = new(StringComparer.Ordinal);
}
