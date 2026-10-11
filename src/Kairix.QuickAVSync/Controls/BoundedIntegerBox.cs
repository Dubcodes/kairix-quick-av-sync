using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kairix.QuickAVSync.Controls;

public sealed class BoundedIntegerBox : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(BoundedIntegerBox),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(int),
        typeof(BoundedIntegerBox),
        new PropertyMetadata(0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(int),
        typeof(BoundedIntegerBox),
        new PropertyMetadata(100, OnRangeChanged));

    public BoundedIntegerBox()
    {
        Text = Value.ToString(CultureInfo.InvariantCulture);
        DataObject.AddPastingHandler(this, OnPaste);
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static bool TryParseValue(string? text, int minimum, int maximum, out int value)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum)
        {
            return true;
        }

        value = default;
        return false;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        CommitText();
        base.OnLostKeyboardFocus(e);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelEdit();
            SelectAll();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    public bool CommitText()
    {
        if (!TryParseValue(Text, Minimum, Maximum, out var parsed))
        {
            RestoreValueText();
            return false;
        }

        SetCurrentValue(ValueProperty, parsed);
        RestoreValueText();
        return true;
    }

    public void CancelEdit() => RestoreValueText();

    private void RestoreValueText() => Text = Value.ToString(CultureInfo.InvariantCulture);

    private static object CoerceValue(DependencyObject dependencyObject, object baseValue)
    {
        var control = (BoundedIntegerBox)dependencyObject;
        return Math.Clamp((int)baseValue, control.Minimum, control.Maximum);
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((BoundedIntegerBox)dependencyObject).RestoreValueText();

    private static void OnRangeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (BoundedIntegerBox)dependencyObject;
        if (control.Minimum > control.Maximum)
        {
            throw new InvalidOperationException("Minimum cannot exceed Maximum.");
        }

        control.CoerceValue(ValueProperty);
        control.RestoreValueText();
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText) ||
            e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text ||
            text.Any(character => !char.IsDigit(character)))
        {
            e.CancelCommand();
        }
    }
}
