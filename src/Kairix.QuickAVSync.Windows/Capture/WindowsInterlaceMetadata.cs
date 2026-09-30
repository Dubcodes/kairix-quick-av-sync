using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed record WindowsInterlaceInfo(bool AttributePresent, int? RawValue, ScanMode ScanMode, FieldOrder FieldOrder, InterlaceLayout Layout);

public static class WindowsInterlaceMetadata
{
    public static WindowsInterlaceInfo Parse(int? rawValue)
    {
        if (rawValue is null) return new(false, null, ScanMode.Unknown, FieldOrder.Unknown, InterlaceLayout.Unknown);
        return rawValue.Value switch
        {
            2 => new(true, 2, ScanMode.Progressive, FieldOrder.Unknown, InterlaceLayout.Unknown),
            3 => new(true, 3, ScanMode.Interlaced, FieldOrder.TopFirst, InterlaceLayout.FullFrame),
            4 => new(true, 4, ScanMode.Interlaced, FieldOrder.BottomFirst, InterlaceLayout.FullFrame),
            5 => new(true, 5, ScanMode.Interlaced, FieldOrder.TopFirst, InterlaceLayout.SingleField),
            6 => new(true, 6, ScanMode.Interlaced, FieldOrder.BottomFirst, InterlaceLayout.SingleField),
            7 => new(true, 7, ScanMode.Unknown, FieldOrder.Unknown, InterlaceLayout.Mixed),
            _ => new(true, rawValue, ScanMode.Unknown, FieldOrder.Unknown, InterlaceLayout.Unknown)
        };
    }
}
