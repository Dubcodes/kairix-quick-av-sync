using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class CaptureFormatCatalogTests
{
    [Theory]
    [InlineData(1920, 1080, 24000, 1001)]
    [InlineData(1920, 1080, 24, 1)]
    [InlineData(1920, 1080, 25, 1)]
    [InlineData(1920, 1080, 30000, 1001)]
    [InlineData(1920, 1080, 30, 1)]
    [InlineData(1920, 1080, 50, 1)]
    [InlineData(1920, 1080, 60000, 1001)]
    [InlineData(1920, 1080, 60, 1)]
    [InlineData(1280, 720, 25, 1)]
    [InlineData(1280, 720, 50, 1)]
    public void CommonHdModesAreRecommended(int width, int height, int numerator, int denominator)
    {
        Assert.Equal(CaptureFormatVisibility.Recommended, CaptureFormatCatalog.Classify(Format(width, height, numerator, denominator)));
    }

    [Theory]
    [InlineData(720, 576, 25, 1)]
    [InlineData(720, 480, 30000, 1001)]
    public void ConventionalSdRastersRemainVisible(int width, int height, int numerator, int denominator)
    {
        Assert.Equal(CaptureFormatVisibility.StandardDefinition, CaptureFormatCatalog.Classify(Format(width, height, numerator, denominator)));
    }

    [Fact]
    public void LowFrameRateIsAdvancedButRemainsInShowAll()
    {
        var auto = CaptureFormatOption.Auto;
        var low = Option("low", Format(1920, 1080, 15, 1));
        Assert.Equal(CaptureFormatVisibility.Advanced, CaptureFormatCatalog.Classify(low.Format!));
        Assert.DoesNotContain(low, CaptureFormatCatalog.VisibleOptions([auto, low], false));
        Assert.Contains(low, CaptureFormatCatalog.VisibleOptions([auto, low], true));
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(1024, 768)]
    [InlineData(1280, 800)]
    [InlineData(1680, 1050)]
    public void ComputerOrientedRastersAreAdvanced(int width, int height)
    {
        Assert.Equal(CaptureFormatVisibility.Advanced, CaptureFormatCatalog.Classify(Format(width, height, 60, 1)));
    }

    [Fact]
    public void ShowAllRetainsEveryOriginalOptionInOrder()
    {
        var options = new[]
        {
            CaptureFormatOption.Auto,
            Option("recommended", Format(1920, 1080, 50, 1)),
            Option("advanced", Format(1024, 768, 15, 1))
        };
        Assert.Equal(options, CaptureFormatCatalog.VisibleOptions(options, true));
    }

    [Fact]
    public void HiddenPersistedSelectionRemainsRepresentable()
    {
        var hidden = Option("saved-advanced", Format(1024, 768, 15, 1));
        var visible = CaptureFormatCatalog.VisibleOptions(
            [CaptureFormatOption.Auto, Option("recommended", Format(1920, 1080, 50, 1)), hidden],
            false,
            hidden.Id);
        Assert.Contains(hidden, visible);
    }

    [Fact]
    public void SavedReconstructionAndFieldOrderWinDuringStartupEnumeration()
    {
        var transport = Format(1920, 1080, 25, 1);
        InterpretationFormatOption[] options =
        [
            new(transport.FrameRate, transport.ScanMode),
            new(transport.FrameRate, transport.ScanMode, true, Rational.From(50), FieldOrder.TopFirst),
            new(transport.FrameRate, transport.ScanMode, true, Rational.From(50), FieldOrder.BottomFirst)
        ];

        var selected = CaptureFormatCatalog.SelectInterpretation(options, transport, null, reconstructFields: true, FieldOrder.BottomFirst);

        Assert.NotNull(selected);
        Assert.True(selected.ReconstructFields);
        Assert.Equal(FieldOrder.BottomFirst, selected.FieldOrder);
    }

    [Fact]
    public void ExplicitReconstructionOffSelectsNormalInterpretation()
    {
        var transport = Format(1920, 1080, 25, 1);
        InterpretationFormatOption[] options =
        [
            new(transport.FrameRate, transport.ScanMode),
            new(transport.FrameRate, transport.ScanMode, true, Rational.From(50), FieldOrder.TopFirst)
        ];

        var selected = CaptureFormatCatalog.SelectInterpretation(options, transport, null, reconstructFields: false, FieldOrder.TopFirst);

        Assert.NotNull(selected);
        Assert.False(selected.ReconstructFields);
    }

    [Fact]
    public void CuratedInputSignalsIncludeHdAndSdBroadcastSet()
    {
        string[] expected =
        [
            "1920x1080-23.976p", "1920x1080-50i", "1920x1080-59.94i",
            "1280x720-23.976p", "1280x720-25p", "1280x720-59.94p",
            "720x576-50i", "720x480-59.94i"
        ];
        Assert.All(expected, id => Assert.Contains(InputSignalOptions.Common, option => option.Id == id));
    }

    private static CaptureFormat Format(int width, int height, int numerator, int denominator) =>
        new(width, height, Rational.From(numerator, denominator), ScanMode.Progressive, FieldOrder.Unknown);

    private static CaptureFormatOption Option(string id, CaptureFormat format) => new(id, format.Display, format);
}
