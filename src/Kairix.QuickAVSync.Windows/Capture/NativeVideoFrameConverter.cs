using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public readonly record struct NativeFrameLayout(int Width, int Height, int Stride, VideoPixelFormat PixelFormat);
public sealed record ConvertedVideoFrame(byte[] Luma, byte[] Bgra);

public static class NativeVideoFrameConverter
{
    public static ConvertedVideoFrame Convert(
        ReadOnlySpan<byte> sourceBytes,
        NativeFrameLayout source,
        int analysisWidth,
        int analysisHeight,
        int presentationWidth,
        int presentationHeight,
        TemporalImageKind temporalImageKind = TemporalImageKind.ProgressiveFrame)
    {
        var luma = new byte[analysisWidth * analysisHeight];
        var bgra = new byte[presentationWidth * presentationHeight * 4];
        for (var y = 0; y < analysisHeight; y++)
        {
            var sy = SourceRow(y, analysisHeight, source.Height, temporalImageKind);
            for (var x = 0; x < analysisWidth; x++)
            {
                var sx = Math.Min(source.Width - 1, x * source.Width / analysisWidth);
                ReadPixel(sourceBytes, source, sx, sy, out _, out _, out _, out var yValue);
                luma[y * analysisWidth + x] = yValue;
            }
        }
        for (var y = 0; y < presentationHeight; y++)
        {
            var sy = SourceRow(y, presentationHeight, source.Height, temporalImageKind);
            for (var x = 0; x < presentationWidth; x++)
            {
                var sx = Math.Min(source.Width - 1, x * source.Width / presentationWidth);
                ReadPixel(sourceBytes, source, sx, sy, out var blue, out var green, out var red, out _);
                var offset = (y * presentationWidth + x) * 4;
                bgra[offset] = blue; bgra[offset + 1] = green; bgra[offset + 2] = red; bgra[offset + 3] = 255;
            }
        }
        return new(luma, bgra);
    }

    private static int SourceRow(int outputRow, int outputHeight, int sourceHeight, TemporalImageKind kind)
    {
        if (kind == TemporalImageKind.ProgressiveFrame) return Math.Min(sourceHeight - 1, outputRow * sourceHeight / outputHeight);
        var parity = kind == TemporalImageKind.BottomField ? 1 : 0;
        var fieldHeight = Math.Max(1, sourceHeight / 2);
        var fieldRow = Math.Min(fieldHeight - 1, outputRow * fieldHeight / outputHeight);
        return Math.Min(sourceHeight - 1, fieldRow * 2 + parity);
    }

    private static void ReadPixel(ReadOnlySpan<byte> bytes, NativeFrameLayout source, int x, int y, out byte blue, out byte green, out byte red, out byte luma)
    {
        blue = green = red = luma = 0;
        var stride = Math.Abs(source.Stride);
        var sourceRow = source.Stride < 0 ? source.Height - 1 - y : y;
        var baseOffset = sourceRow * stride;
        if (source.PixelFormat == VideoPixelFormat.Nv12)
        {
            var yOffset = baseOffset + x;
            if (!Has(bytes.Length, yOffset, 1)) return;
            luma = bytes[yOffset];
            var uvOffset = stride * source.Height + y / 2 * stride + x / 2 * 2;
            if (!Has(bytes.Length, uvOffset, 2)) { blue = green = red = luma; return; }
            WindowsColorConversion.YuvToBgr(luma, bytes[uvOffset], bytes[uvOffset + 1], out blue, out green, out red);
            return;
        }
        if (source.PixelFormat is VideoPixelFormat.Yuy2 or VideoPixelFormat.Uyvy)
        {
            var pair = baseOffset + (x & ~1) * 2;
            if (!Has(bytes.Length, pair, 4)) return;
            byte u, v;
            if (source.PixelFormat == VideoPixelFormat.Yuy2) { luma = bytes[pair + (x & 1) * 2]; u = bytes[pair + 1]; v = bytes[pair + 3]; }
            else { luma = bytes[pair + 1 + (x & 1) * 2]; u = bytes[pair]; v = bytes[pair + 2]; }
            WindowsColorConversion.YuvToBgr(luma, u, v, out blue, out green, out red);
            return;
        }
        var bytesPerPixel = source.PixelFormat == VideoPixelFormat.Bgra32 ? 4 : 3;
        var offset = baseOffset + x * bytesPerPixel;
        if (!Has(bytes.Length, offset, bytesPerPixel)) return;
        blue = bytes[offset]; green = bytes[offset + 1]; red = bytes[offset + 2];
        luma = (byte)Math.Clamp((red * 54 + green * 183 + blue * 19) >> 8, 0, 255);
    }

    private static bool Has(int length, int offset, int count) => offset >= 0 && offset + count <= length;
}
