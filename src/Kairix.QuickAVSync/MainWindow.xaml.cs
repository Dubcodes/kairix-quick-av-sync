using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kairix.QuickAVSync.ViewModels;

namespace Kairix.QuickAVSync;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new(); private bool _closing;
    public MainWindow() { InitializeComponent(); DataContext = _viewModel; Loaded += async (_, _) => await _viewModel.InitializeAsync(); }
    private static bool EditingText() => Keyboard.FocusedElement is System.Windows.Controls.TextBox;
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (EditingText()) return; if (e.Key == Key.H) { _viewModel.IsHold = true; e.Handled = true; return; } if (e.IsRepeat) return;
        switch (e.Key) { case Key.Space: _viewModel.ManualClapCommand.Execute(null); e.Handled = true; break; case Key.Left: if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _viewModel.StepCoarse(-1); else _viewModel.StepPreviousCommand.Execute(null); e.Handled = true; break; case Key.Right: if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _viewModel.StepCoarse(1); else _viewModel.StepNextCommand.Execute(null); e.Handled = true; break; case Key.Enter: _viewModel.MarkVisualCommand.Execute(null); e.Handled = true; break; case Key.A: _viewModel.MarkAudioAtPlayhead(); e.Handled = true; break; case Key.R: _viewModel.ResumeLiveCommand.Execute(null); e.Handled = true; break; case Key.F5: _viewModel.ReconnectCommand.Execute(null); e.Handled = true; break; }
    }
    private void Window_KeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.H) { _viewModel.IsHold = false; e.Handled = true; } }
    private void Hold_Down(object sender, MouseButtonEventArgs e) { _viewModel.IsHold = true; ((ButtonBase)sender).CaptureMouse(); }
    private void Hold_Up(object sender, MouseButtonEventArgs e) { _viewModel.IsHold = false; ((ButtonBase)sender).ReleaseMouseCapture(); }
    private void Waveform_AudioPointSelected(object? sender, double relativeMs) => _viewModel.SelectAudioPoint(relativeMs);
    private async void Window_Closing(object? sender, CancelEventArgs e) { if (_closing) return; e.Cancel = true; _closing = true; await _viewModel.DisposeAsync(); Close(); }
}
