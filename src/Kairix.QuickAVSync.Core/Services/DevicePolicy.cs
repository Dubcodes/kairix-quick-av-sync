using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed class DevicePairingService
{
    public DevicePairing Pair(CaptureDeviceDescriptor video, IReadOnlyList<AudioEndpointDescriptor> audio)
    {
        var active = audio.Where(a => a.IsActive).ToArray();
        if (!string.IsNullOrWhiteSpace(video.ContainerId))
        {
            var exact = active.Where(a => string.Equals(a.ContainerId, video.ContainerId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length == 1) return new(exact[0], PairingConfidence.ExactContainer, "Shared physical device Container ID");
            if (exact.Length > 1)
            {
                var selected = exact.OrderByDescending(a => NameSimilarity(video.FriendlyName, a.FriendlyName)).ThenBy(a => a.Id, StringComparer.Ordinal).First();
                return new(selected, PairingConfidence.ExactContainer, "Shared Container ID; deterministic closest endpoint name");
            }
        }
        if (!string.IsNullOrWhiteSpace(video.ParentId))
        {
            var parent = active.Where(a => string.Equals(a.ParentId, video.ParentId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (parent.Length == 1) return new(parent[0], PairingConfidence.HardwareParent, "Shared hardware parent identity");
        }
        var safe = active.Where(a => !a.IsDefaultMicrophone && NameSimilarity(video.FriendlyName, a.FriendlyName) >= 2).ToArray();
        return safe.Length == 1
            ? new(safe[0], PairingConfidence.NameFallback, "Unique cautious capture-device name match; physical identity unavailable")
            : new(null, PairingConfidence.None, safe.Length > 1 ? "Ambiguous name-based audio matches" : "Paired audio endpoint not found with sufficient certainty");
    }
    private static int NameSimilarity(string a, string b)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio", "video", "device", "capture", "input", "usb", "endpoint" };
        var left = Tokens(a).Where(x => !ignored.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Tokens(b).Count(left.Contains);
    }
    private static IEnumerable<string> Tokens(string value) => value.Split([' ', '-', '_', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
}

public sealed class CaptureDeviceRanker
{
    public IReadOnlyList<CaptureDeviceDescriptor> Rank(IEnumerable<CaptureDeviceDescriptor> devices, string? rememberedId, string? rememberedName)
        => devices.OrderByDescending(d => Score(d, rememberedId, rememberedName)).ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal).ToArray();

    public CaptureDeviceDescriptor? SelectBest(IEnumerable<CaptureDeviceDescriptor> devices, string? rememberedId, string? rememberedName)
    {
        var list = devices.Where(d => d.IsAvailable).ToArray();
        var exact = list.FirstOrDefault(d => d.Id == rememberedId); if (exact is not null) return exact;
        var safeName = list.Where(d => d.Kind == CaptureDeviceKind.ExternalCapture && d.FriendlyName == rememberedName).ToArray();
        if (safeName.Length == 1) return safeName[0];
        return Rank(list, null, null).FirstOrDefault(d => d.Kind is CaptureDeviceKind.ExternalCapture or CaptureDeviceKind.Synthetic)
            ?? (list.Length == 1 ? list[0] : null);
    }

    public int Score(CaptureDeviceDescriptor d, string? rememberedId, string? rememberedName)
    {
        var score = d.Kind switch { CaptureDeviceKind.ExternalCapture => 500, CaptureDeviceKind.Synthetic => 100, CaptureDeviceKind.Unknown => 50, CaptureDeviceKind.IntegratedCamera => 0, _ => 0 };
        if (d.Id == rememberedId) score += 10_000;
        else if (d.Kind == CaptureDeviceKind.ExternalCapture && d.FriendlyName == rememberedName) score += 500;
        if (!d.IsAvailable) score -= 20_000;
        if (!string.IsNullOrWhiteSpace(d.ContainerId)) score += 20;
        return score;
    }
}

public static class CaptureFormatSelector
{
    public static CaptureFormat? Select(IEnumerable<CaptureFormat> formats) => formats
        .OrderByDescending(f => f.Width == 1920 && f.Height == 1080)
        .ThenByDescending(f => RatePriority(f.FrameRate.Value))
        .ThenByDescending(f => f.Width * f.Height)
        .ThenByDescending(f => f.FrameRate.Value).FirstOrDefault();
    private static int RatePriority(double rate) => Math.Abs(rate - 50) < .02 ? 5 : Math.Abs(rate - 25) < .02 ? 4 : Math.Abs(rate - 59.94) < .02 ? 3 : Math.Abs(rate - 29.97) < .02 ? 2 : 1;
}
