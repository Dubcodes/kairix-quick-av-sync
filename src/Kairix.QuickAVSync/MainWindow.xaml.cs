using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kairix.QuickAVSync.ViewModels;
using Kairix.QuickAVSync.Windows.Infrastructure;

namespace Kairix.QuickAVSync;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new(); private readonly AppLogger _shutdownLog = new(); private bool _closing; private bool _shutdownComplete;
    public MainWindow() { InitializeComponent(); DataContext = _viewModel; Loaded += async (_, _) => await _viewModel.InitializeAsync(); }
    private static bool EditingText() => Keyboard.FocusedElement is System.Windows.Controls.TextBox;
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (EditingText()) return; if (e.Key == Key.H) { if (!e.IsRepeat && _viewModel.IsArmEnabled) _viewModel.IsHold = !_viewModel.IsHold; e.Handled = true; return; } if (e.IsRepeat) return;
        switch (e.Key) { case Key.Space: _viewModel.ManualClapCommand.Execute(null); e.Handled = true; break; case Key.Left: if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) _viewModel.StepAudio(-1); else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _viewModel.StepCoarse(-1); else _viewModel.StepPreviousCommand.Execute(null); e.Handled = true; break; case Key.Right: if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) _viewModel.StepAudio(1); else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _viewModel.StepCoarse(1); else _viewModel.StepNextCommand.Execute(null); e.Handled = true; break; case Key.Enter: _viewModel.MarkVisualCommand.Execute(null); e.Handled = true; break; case Key.R: _viewModel.ResumeLiveCommand.Execute(null); e.Handled = true; break; case Key.F5: _viewModel.ReconnectCommand.Execute(null); e.Handled = true; break; }
    }
    private void Window_KeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.H) e.Handled = true; }
    private void Waveform_PlayheadSelected(object? sender, double relativeMs) => _viewModel.MovePlayheadTo(relativeMs);
    private void Waveform_AudioPointPreviewed(object? sender, double relativeMs) => _viewModel.PreviewAudioPoint(relativeMs);
    private void Waveform_AudioPointCommitted(object? sender, double relativeMs) => _viewModel.SelectAudioPoint(relativeMs);
    private void Waveform_FrameStepRequested(object? sender, int amount) => _viewModel.StepTimeline(amount);
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _shutdownLog.Write("shutdown", "Window close requested; disposing application services.");
        try
        {
            await _viewModel.DisposeAsync();
            _shutdownLog.Write("shutdown", "Application services disposed; completing window close.");
        }
        catch (Exception ex)
        {
            _shutdownLog.Write("shutdown.failure", ex.ToString());
        }
        finally
        {
            _shutdownComplete = true;
            Application.Current.Shutdown();
        }
    }
}
