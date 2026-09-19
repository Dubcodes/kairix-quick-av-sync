namespace Kairix.QuickAVSync.Models;

public enum TimingQuality { DeviceQpc, StreamTimestamp, ArrivalFallback }
public enum ScanMode { Progressive, Interlaced }
public enum FieldOrder { Unknown, TopFirst, BottomFirst }

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

public sealed record CaptureFormat(int Width, int Height, Rational FrameRate, ScanMode ScanMode, FieldOrder FieldOrder, int AudioSampleRate = 48000)
{
    public TimeSpan TemporalImageDuration => ScanMode == ScanMode.Interlaced
        ? TimeSpan.FromTicks(FrameRate.FrameDuration.Ticks / 2) : FrameRate.FrameDuration;
    public string Display => $"{Width}×{Height} · {FrameRate.Value:0.##}{(ScanMode == ScanMode.Interlaced ? "i" : "p")} · {AudioSampleRate / 1000} kHz";
}

public readonly record struct MediaTimestamp(long Ticks100ns, TimingQuality Quality)
{
    public TimeSpan Time => TimeSpan.FromTicks(Ticks100ns);
    public static MediaTimestamp FromTimeSpan(TimeSpan time, TimingQuality quality) => new(time.Ticks, quality);
}

public sealed record AudioChunk(MediaTimestamp Timestamp, float[] Samples, int SampleRate, int Channels)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / Channels / SampleRate);
}

public sealed record VideoFrame(MediaTimestamp Timestamp, int Width, int Height, byte[] Luma, int TemporalIndex, bool IsField = false, bool TopField = false);
public sealed record AudioTransient(MediaTimestamp Timestamp, float Peak, float NoiseFloor);
public sealed record VisualCandidate(MediaTimestamp Timestamp, int TemporalIndex, double Confidence, double MotionScore, VideoFrame Frame);
public sealed record CaptureDevice(string Id, string FriendlyName, string? ContainerId, bool IsLikelyExternal, int Rank)
{
    public override string ToString() => FriendlyName;
}
public sealed record AudioEndpoint(string Id, string FriendlyName, string? ContainerId, bool IsDefaultMicrophone = false);
public sealed record DevicePairing(AudioEndpoint? Endpoint, bool IsCertain, string Reason);
public sealed record SyncResult(double SignedMilliseconds, string Wording)
{
    public static SyncResult Calculate(MediaTimestamp audio, MediaTimestamp visual)
    {
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
    public double RollingBufferSeconds { get; set; } = 5;
    public double WorkWindowMilliseconds { get; set; } = 250;
}
