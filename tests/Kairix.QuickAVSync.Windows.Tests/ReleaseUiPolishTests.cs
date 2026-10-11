using System.ComponentModel;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Kairix.QuickAVSync.Controls;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Windows.Infrastructure;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class ReleaseUiPolishTests
{
    private static string UiPath => Path.Combine(AppContext.BaseDirectory, "Ui", "MainWindow.xaml");
    private static string AppUiPath => Path.Combine(AppContext.BaseDirectory, "Ui", "App.xaml");

    [Fact]
    public void DetectionIsTheFirstAndExplicitDefaultSettingsPage()
    {
        var document = System.Xml.Linq.XDocument.Load(UiPath);
        var settings = document.Descendants().Single(element =>
            element.Name.LocalName == "TabControl" && AutomationId(element) == "SettingsTabs");
        var tabs = settings.Elements().Where(element => element.Name.LocalName == "TabItem").ToArray();

        Assert.Equal("0", settings.Attribute("SelectedIndex")?.Value);
        Assert.Equal(
            ["DetectionTab", "AppearanceTab", "ReviewTab", "CaptureTab", "ShortcutsTab", "AboutTab"],
            tabs.Select(AutomationId));
    }

    [Fact]
    public void SharedComboBoxTemplateUsesAFullSurfacePressTarget()
    {
        var document = System.Xml.Linq.XDocument.Load(AppUiPath);
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var toggle = document.Descendants().Single(element =>
            element.Name.LocalName == "ToggleButton" && element.Attribute(x + "Name")?.Value == "DropDownToggle");

        Assert.Equal("2", toggle.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Equal("Press", toggle.Attribute("ClickMode")?.Value);
        Assert.Equal("False", toggle.Attribute("Focusable")?.Value);
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("47", true, 47)]
    [InlineData("100", true, 100)]
    [InlineData("-1", false, 0)]
    [InlineData("101", false, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("value", false, 0)]
    public void SensitivityInputAcceptsOnlyBoundedIntegers(string text, bool expected, int expectedValue)
    {
        Assert.Equal(expected, BoundedIntegerBox.TryParseValue(text, 0, 100, out var actual));
        Assert.Equal(expectedValue, actual);
    }

    [Fact]
    public void SensitivityInputCommitsValidTextAndRejectsInvalidText()
    {
        RunSta(() =>
        {
            var input = new BoundedIntegerBox { Minimum = 0, Maximum = 100, Value = 50 };
            input.Text = "83";
            Assert.True(input.CommitText());
            Assert.Equal(83, input.Value);

            input.Text = "101";
            Assert.False(input.CommitText());
            Assert.Equal(83, input.Value);
            Assert.Equal("83", input.Text);

            input.Text = "12";
            input.CancelEdit();
            Assert.Equal("83", input.Text);
        });
    }

    [Fact]
    public void SliderAndNumericInputStaySynchronizedThroughTheirSharedSetting()
    {
        RunSta(() =>
        {
            var setting = new SensitivitySetting { Value = 50 };
            var slider = new Slider { Minimum = 0, Maximum = 100 };
            var input = new BoundedIntegerBox { Minimum = 0, Maximum = 100 };
            slider.SetBinding(Slider.ValueProperty, new Binding(nameof(SensitivitySetting.Value)) { Source = setting, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            input.SetBinding(BoundedIntegerBox.ValueProperty, new Binding(nameof(SensitivitySetting.Value)) { Source = setting, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

            slider.Value = 72;
            Assert.Equal(72, setting.Value);
            Assert.Equal(72, input.Value);

            input.Text = "31";
            Assert.True(input.CommitText());
            Assert.Equal(31, setting.Value);
            Assert.Equal(31d, slider.Value);
        });
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void SensitivityBoundsPersist(int visual, int audio)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-release-ui-{Guid.NewGuid():N}.json");
        try
        {
            using var service = new SettingsService(path);
            service.Save(new AppSettings { VisualSensitivity = visual, AudioSensitivity = audio });
            var loaded = service.Load();
            Assert.Equal(visual, loaded.VisualSensitivity);
            Assert.Equal(audio, loaded.AudioSensitivity);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ShortcutPageKeepsEveryAdvertisedShortcutAndEveryRowExplainsItself()
    {
        var document = System.Xml.Linq.XDocument.Load(UiPath);
        var rows = document.Descendants().Where(element =>
            element.Name.LocalName == "Border" && AutomationId(element)?.StartsWith("ShortcutRow", StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(
            ["ShortcutRowSpace", "ShortcutRowEnter", "ShortcutRowLeft", "ShortcutRowRight", "ShortcutRowShiftLeft", "ShortcutRowShiftRight", "ShortcutRowCtrlLeft", "ShortcutRowCtrlRight", "ShortcutRowA", "ShortcutRowH", "ShortcutRowR", "ShortcutRowF5"],
            rows.Select(AutomationId));
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Attribute("ToolTip")?.Value)));
    }

    [Fact]
    public void HistoryEntriesRemainClearlyClickableReviewCommands()
    {
        var ui = File.ReadAllText(UiPath);
        Assert.Contains("AutomationProperties.AutomationId=\"HistoryResultButton\"", ui);
        Assert.Contains("Command=\"{Binding DataContext.SelectHistoryCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", ui);
        Assert.Contains("Select an event to reopen its retained review", ui);
        Assert.Contains("Text=\"OPEN  ›\"", ui);
        Assert.Contains("Hold automatic detection and review this retained event", ui);
    }

    private static string? AutomationId(System.Xml.Linq.XElement element)
    {
        System.Xml.Linq.XNamespace automation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        return element.Attribute(automation + "AutomationProperties.AutomationId")?.Value ??
               element.Attributes().FirstOrDefault(attribute =>
                   attribute.Name.LocalName is "AutomationProperties.AutomationId" or "AutomationId")?.Value;
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception failure) { exception = failure; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null) ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private sealed class SensitivitySetting : INotifyPropertyChanged
    {
        private int _value;
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
}
