namespace Kairix.QuickAVSync.Services;

/// <summary>Display-only waveform scaling. It never feeds capture or detection paths.</summary>
public static class WaveformDisplayTransform
{
    public const string AutoGain = "Auto Gain";
    public const string Linear = "Linear";
    public const string Db60 = "dB · 60 dB";
    public const string Db96 = "dB · 96 dB";
    public const double MinimumUsefulPeak = 0.0031622776601683794; // -50 dBFS

    public static IReadOnlyList<string> Amplitudes { get; } = [AutoGain, Linear, Db60, Db96];

    public static string NormalizeAmplitude(string? amplitude) => Amplitudes.Contains(amplitude, StringComparer.Ordinal) ? amplitude! : AutoGain;

    public static string NormalizeStyle(string? style) => style switch
    {
        "Mirrored" => "Centered Bars",
        "Filled" => "Centered Fill",
        "Line" => "Centered Line",
        "Peaks" => "Peak Bars",
        "Filled Peaks" => "Peak Fill",
        "Centered Fill" or "Centered Bars" or "Centered Line" or "Peak Fill" or "Peak Bars" or "Peak Line" => style,
        _ => "Centered Fill"
    };

    public static double Decibels(double sample, double floorDb)
    {
        if (!double.IsFinite(sample) || sample <= 0) return floorDb;
        return Math.Max(floorDb, 20 * Math.Log10(Math.Abs(sample)));
    }

    public static float[] Transform(IReadOnlyList<float> samples, string? amplitude)
    {
        var normalized = NormalizeAmplitude(amplitude);
        var output = new float[samples.Count];
        if (normalized == AutoGain)
        {
            var peak = samples.Count == 0 ? 0d : samples.Max(sample => Math.Abs((double)sample));
            var gain = peak >= MinimumUsefulPeak ? .9 / peak : 1d;
            for (var index = 0; index < samples.Count; index++) output[index] = (float)Math.Clamp(Math.Abs((double)samples[index]) * gain, 0, 1);
            return output;
        }
        if (normalized == Linear)
        {
            for (var index = 0; index < samples.Count; index++) output[index] = (float)Math.Clamp(Math.Abs((double)samples[index]), 0, 1);
            return output;
        }
        var floor = normalized == Db60 ? -60d : -96d;
        for (var index = 0; index < samples.Count; index++) output[index] = (float)Math.Clamp((Decibels(samples[index], floor) - floor) / -floor, 0, 1);
        return output;
    }
}
