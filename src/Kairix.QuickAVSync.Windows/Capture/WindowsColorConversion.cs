namespace Kairix.QuickAVSync.Windows.Capture;

public static class WindowsColorConversion
{
    // BT.601 limited-range conversion used by the directly supported YUV source layouts.
    public static void YuvToBgr(byte y, byte u, byte v, out byte blue, out byte green, out byte red)
    {
        var c = Math.Max(0, y - 16); var d = u - 128; var e = v - 128;
        red = Clamp((298 * c + 409 * e + 128) >> 8); green = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8); blue = Clamp((298 * c + 516 * d + 128) >> 8);
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
