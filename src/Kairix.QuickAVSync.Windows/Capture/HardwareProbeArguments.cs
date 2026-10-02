using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed record HardwareProbeArguments(
    string? DeviceName = null,
    string? DeviceId = null,
    string? ModeId = null,
    string? SourceId = null,
    bool ReconstructFields = false,
    string FieldOrder = "top",
    bool ListFormats = false,
    bool AnalyzeSignal = false,
    bool TimingDetail = false,
    int DetectionWidth = 640,
    int DetectionHeight = 360,
    int ReviewWidth = 160,
    int ReviewHeight = 90,
    bool Help = false)
{
    public static HardwareProbeArguments Parse(IReadOnlyList<string> arguments)
    {
        string? device = null, deviceId = null, mode = null, source = null, fieldOrder = null, positionalDevice = null;
        string? detectionResolution = null, reviewResolution = null;
        var list = false; var analyze = false; var timing = false; var reconstruct = false; var help = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument.ToLowerInvariant())
            {
                case "--device": device = ReadValue(arguments, ref index, argument, device); break;
                case "--device-id": deviceId = ReadValue(arguments, ref index, argument, deviceId); break;
                case "--mode": mode = ReadValue(arguments, ref index, argument, mode); break;
                case "--source": source = ReadValue(arguments, ref index, argument, source); break;
                case "--reconstruct-fields": SetFlag(ref reconstruct, argument); break;
                case "--field-order": fieldOrder = ReadValue(arguments, ref index, argument, fieldOrder); break;
                case "--list-formats": SetFlag(ref list, argument); break;
                case "--analyze-signal": SetFlag(ref analyze, argument); break;
                case "--timing-detail": SetFlag(ref timing, argument); break;
                case "--detection-resolution": detectionResolution = ReadValue(arguments, ref index, argument, detectionResolution); break;
                case "--review-resolution": reviewResolution = ReadValue(arguments, ref index, argument, reviewResolution); break;
                case "--help" or "-h" or "/?": SetFlag(ref help, argument); break;
                default:
                    if (argument.StartsWith('-')) throw new ArgumentException($"Unknown argument '{argument}'. Use --help for supported syntax.");
                    if (positionalDevice is not null) throw new ArgumentException("Only one positional device name is supported. Prefer --device \"<friendly name>\".");
                    positionalDevice = argument;
                    break;
            }
        }
        if (device is not null && positionalDevice is not null) throw new ArgumentException("Specify the friendly name using either --device or one positional argument, not both.");
        if (deviceId is not null && (device is not null || positionalDevice is not null)) throw new ArgumentException("Specify either --device/positional name or --device-id, not both.");
        fieldOrder ??= "top";
        if (fieldOrder is not ("top" or "bottom")) throw new ArgumentException("--field-order must be 'top' or 'bottom'.");
        var detection = ParseResolution(detectionResolution ?? "640x360", "--detection-resolution");
        var review = ParseResolution(reviewResolution ?? "160x90", "--review-resolution");
        return new(device ?? positionalDevice, deviceId, mode, source, reconstruct, fieldOrder, list, analyze, timing,
            detection.Width, detection.Height, review.Width, review.Height, help);
    }

    public CaptureDeviceDescriptor ResolveDevice(IReadOnlyList<CaptureDeviceDescriptor> devices)
    {
        var available = devices.Where(candidate => candidate.IsAvailable).ToArray();
        if (DeviceId is not null)
        {
            var matches = available.Where(candidate => string.Equals(candidate.Id, DeviceId, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidOperationException(matches.Length == 0
                ? $"Requested device ID was not found: '{DeviceId}'."
                : $"Device ID resolved ambiguously: '{DeviceId}'.");
        }
        if (DeviceName is not null)
        {
            var matches = available.Where(candidate => string.Equals(candidate.FriendlyName, DeviceName, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException($"Requested device was not found: '{DeviceName}'."),
                _ => throw new InvalidOperationException($"Requested device name is ambiguous: '{DeviceName}'. Use --device-id. Matching IDs:{Environment.NewLine}{string.Join(Environment.NewLine, matches.Select(candidate => candidate.Id))}")
            };
        }
        if (available.Length == 1) return available[0];
        if (available.Length == 0) throw new InvalidOperationException("No available capture devices were found.");
        throw new InvalidOperationException($"Multiple capture devices are available; select one with --device or --device-id:{Environment.NewLine}{string.Join(Environment.NewLine, available.Select(candidate => $"- {candidate.FriendlyName} [{candidate.Id}]"))}");
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string option, string? existing)
    {
        if (existing is not null) throw new ArgumentException($"Argument '{option}' was specified more than once.");
        if (++index >= arguments.Count || arguments[index].StartsWith('-')) throw new ArgumentException($"Argument '{option}' requires a value.");
        return arguments[index];
    }

    private static void SetFlag(ref bool field, string option)
    {
        if (field) throw new ArgumentException($"Argument '{option}' was specified more than once.");
        field = true;
    }

    private static (int Width, int Height) ParseResolution(string value, string option)
    {
        var parts = value.Split('x', 'X');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height)
            || width is < 16 or > 7680 || height is < 16 or > 4320)
            throw new ArgumentException($"{option} must be a WxH raster between 16x16 and 7680x4320.");
        return (width, height);
    }
}
