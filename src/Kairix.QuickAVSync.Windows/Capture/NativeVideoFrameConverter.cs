using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public readonly record struct NativeFrameLayout(int Width, int Height, int Stride, VideoPixelFormat PixelFormat);
public sealed record ConvertedVideoFrame(byte[] Luma, byte[] Bgra);

public sealed class NativeVideoConversionPlan
{
    internal NativeVideoConversionPlan(NativeFrameLayout source, int analysisWidth, int analysisHeight, int presentationWidth, int presentationHeight,
        TemporalImageKind kind, int[] analysisX, int[] analysisY, int[] presentationX, int[] presentationY)
    {
        Source = source; AnalysisWidth = analysisWidth; AnalysisHeight = analysisHeight; PresentationWidth = presentationWidth; PresentationHeight = presentationHeight;
        TemporalImageKind = kind; AnalysisX = analysisX; AnalysisY = analysisY; PresentationX = presentationX; PresentationY = presentationY;
    }

    public NativeFrameLayout Source { get; }
    public int AnalysisWidth { get; }
    public int AnalysisHeight { get; }
    public int PresentationWidth { get; }
    public int PresentationHeight { get; }
    public TemporalImageKind TemporalImageKind { get; }
    internal int[] AnalysisX { get; }
    internal int[] AnalysisY { get; }
    internal int[] PresentationX { get; }
    internal int[] PresentationY { get; }
}

public static class NativeVideoFrameConverter
{
    public static NativeVideoConversionPlan CreatePlan(NativeFrameLayout source, int analysisWidth, int analysisHeight, int presentationWidth, int presentationHeight,
        TemporalImageKind temporalImageKind = TemporalImageKind.ProgressiveFrame)
    {
        if (source.Width <= 0 || source.Height <= 0 || analysisWidth <= 0 || analysisHeight <= 0 || presentationWidth <= 0 || presentationHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(source), "Source and output dimensions must be positive.");
        return new(source, analysisWidth, analysisHeight, presentationWidth, presentationHeight, temporalImageKind,
            XMap(source.Width, analysisWidth), YMap(source.Height, analysisHeight, temporalImageKind),
            XMap(source.Width, presentationWidth), YMap(source.Height, presentationHeight, temporalImageKind));
    }

    public static ConvertedVideoFrame Convert(ReadOnlySpan<byte> sourceBytes, NativeVideoConversionPlan plan)
    {
        var luma = new byte[plan.AnalysisWidth * plan.AnalysisHeight];
        var bgra = new byte[plan.PresentationWidth * plan.PresentationHeight * 4];
        for (var y = 0; y < plan.AnalysisHeight; y++)
        {
            var sy = plan.AnalysisY[y];
            for (var x = 0; x < plan.AnalysisWidth; x++) luma[y * plan.AnalysisWidth + x] = ReadLuma(sourceBytes, plan.Source, plan.AnalysisX[x], sy);
        }
        for (var y = 0; y < plan.PresentationHeight; y++)
        {
            var sy = plan.PresentationY[y];
            for (var x = 0; x < plan.PresentationWidth; x++)
            {
                ReadPixel(sourceBytes, plan.Source, plan.PresentationX[x], sy, out var blue, out var green, out var red, out _);
                var offset = (y * plan.PresentationWidth + x) * 4;
                bgra[offset] = blue; bgra[offset + 1] = green; bgra[offset + 2] = red; bgra[offset + 3] = 255;
            }
        }
        return new(luma, bgra);
    }

    public static ConvertedVideoFrame Convert(ReadOnlySpan<byte> sourceBytes, NativeFrameLayout source, int analysisWidth, int analysisHeight,
        int presentationWidth, int presentationHeight, TemporalImageKind temporalImageKind = TemporalImageKind.ProgressiveFrame) =>
        Convert(sourceBytes, CreatePlan(source, analysisWidth, analysisHeight, presentationWidth, presentationHeight, temporalImageKind));

    private static int[] XMap(int sourceWidth, int outputWidth)
    {
        var map = new int[outputWidth];
        for (var x = 0; x < map.Length; x++) map[x] = Math.Min(sourceWidth - 1, x * sourceWidth / outputWidth);
        return map;
    }

    private static int[] YMap(int sourceHeight, int outputHeight, TemporalImageKind kind)
    {
        var map = new int[outputHeight];
        for (var y = 0; y < map.Length; y++) map[y] = SourceRow(y, outputHeight, sourceHeight, kind);
        return map;
    }

    private static int SourceRow(int outputRow, int outputHeight, int sourceHeight, TemporalImageKind kind)
    {
        if (kind == TemporalImageKind.ProgressiveFrame) return Math.Min(sourceHeight - 1, outputRow * sourceHeight / outputHeight);
        var parity = kind == TemporalImageKind.BottomField ? 1 : 0;
        var fieldHeight = Math.Max(1, sourceHeight / 2);
        var fieldRow = Math.Min(fieldHeight - 1, outputRow * fieldHeight / outputHeight);
        return Math.Min(sourceHeight - 1, fieldRow * 2 + parity);
    }

    private static byte ReadLuma(ReadOnlySpan<byte> bytes, NativeFrameLayout source, int x, int y)
    {
        var stride = Math.Abs(source.Stride);
        var sourceRow = source.Stride < 0 ? source.Height - 1 - y : y;
        var baseOffset = sourceRow * stride;
        if (source.PixelFormat == VideoPixelFormat.Nv12)
        {
            var offset = baseOffset + x;
            return Has(bytes.Length, offset, 1) ? bytes[offset] : (byte)0;
        }
        if (source.PixelFormat == VideoPixelFormat.Yuy2)
        {
            var offset = baseOffset + (x & ~1) * 2 + (x & 1) * 2;
            return Has(bytes.Length, offset, 1) ? bytes[offset] : (byte)0;
        }
        if (source.PixelFormat == VideoPixelFormat.Uyvy)
        {
            var offset = baseOffset + (x & ~1) * 2 + 1 + (x & 1) * 2;
            return Has(bytes.Length, offset, 1) ? bytes[offset] : (byte)0;
        }
        var bytesPerPixel = source.PixelFormat == VideoPixelFormat.Bgra32 ? 4 : 3;
        var rgbOffset = baseOffset + x * bytesPerPixel;
        if (!Has(bytes.Length, rgbOffset, bytesPerPixel)) return 0;
        var blue = bytes[rgbOffset]; var green = bytes[rgbOffset + 1]; var red = bytes[rgbOffset + 2];
        return (byte)Math.Clamp((red * 54 + green * 183 + blue * 19) >> 8, 0, 255);
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
