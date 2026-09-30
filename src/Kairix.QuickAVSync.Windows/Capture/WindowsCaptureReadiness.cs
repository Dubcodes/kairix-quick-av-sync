using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public static class WindowsCaptureReadiness
{
    public static string Describe(bool videoValidated, bool pairedAudio, bool audioRunning, bool audioFailed, TimingQuality videoTiming)
    {
        if (!videoValidated) return "Waiting for video";
        if (!pairedAudio) return "Video live — embedded audio not found";
        if (audioFailed) return "Video live — embedded audio unavailable";
        if (!audioRunning) return "Video live — waiting for embedded audio";
        if (videoTiming is not (TimingQuality.DeviceHardware or TimingQuality.ClockCorrelated)) return "Video live — timing not comparable";
        return "Ready to clap";
    }
}
