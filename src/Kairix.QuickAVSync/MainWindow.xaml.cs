using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kairix.QuickAVSync.ViewModels;
using Kairix.QuickAVSync.Windows.Infrastructure;

namespace Kairix.QuickAVSync;

public enum MainWindowShortcut
{
    None,
    ManualClap,
    MarkVisual,
    PreviousVideo,
    NextVideo,
    CoarsePreviousVideo,
    CoarseNextVideo,
    PreviousAudio,
    NextAudio,
    MarkAudioAtPlayhead,
    ToggleHold,
    LiveView,
    Reconnect
}

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new(); private readonly AppLogger _shutdownLog = new(); private bool _closing; private bool _shutdownComplete;
    public MainWindow() { InitializeComponent(); DataContext = _viewModel; Loaded += Window_Loaded; }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.InitializeAsync(); }
        catch (Exception ex) { _shutdownLog.Write("startup.failure", ex.ToString()); }
    }
    private static bool EditingText() => Keyboard.FocusedElement is System.Windows.Controls.TextBox;
    public static MainWindowShortcut ResolveShortcut(Key key, ModifierKeys modifiers) => key switch
    {
        Key.Space => MainWindowShortcut.ManualClap,
        Key.Enter => MainWindowShortcut.MarkVisual,
        Key.Left when (modifiers & ModifierKeys.Control) != 0 => MainWindowShortcut.PreviousAudio,
        Key.Right when (modifiers & ModifierKeys.Control) != 0 => MainWindowShortcut.NextAudio,
        Key.Left when (modifiers & ModifierKeys.Shift) != 0 => MainWindowShortcut.CoarsePreviousVideo,
        Key.Right when (modifiers & ModifierKeys.Shift) != 0 => MainWindowShortcut.CoarseNextVideo,
        Key.Left => MainWindowShortcut.PreviousVideo,
        Key.Right => MainWindowShortcut.NextVideo,
        Key.A => MainWindowShortcut.MarkAudioAtPlayhead,
        Key.H => MainWindowShortcut.ToggleHold,
        Key.R => MainWindowShortcut.LiveView,
        Key.F5 => MainWindowShortcut.Reconnect,
        _ => MainWindowShortcut.None
    };

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (EditingText()) return;
        var shortcut = ResolveShortcut(e.Key, Keyboard.Modifiers);
        if (shortcut == MainWindowShortcut.None) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        switch (shortcut)
        {
            case MainWindowShortcut.ManualClap: _viewModel.ManualClapCommand.Execute(null); break;
            case MainWindowShortcut.MarkVisual: _viewModel.MarkVisualCommand.Execute(null); break;
            case MainWindowShortcut.PreviousVideo: _viewModel.StepPreviousCommand.Execute(null); break;
            case MainWindowShortcut.NextVideo: _viewModel.StepNextCommand.Execute(null); break;
            case MainWindowShortcut.CoarsePreviousVideo: _viewModel.StepCoarse(-1); break;
            case MainWindowShortcut.CoarseNextVideo: _viewModel.StepCoarse(1); break;
            case MainWindowShortcut.PreviousAudio: _viewModel.StepAudio(-1); break;
            case MainWindowShortcut.NextAudio: _viewModel.StepAudio(1); break;
            case MainWindowShortcut.MarkAudioAtPlayhead: _viewModel.MarkAudioAtPlayhead(); break;
            case MainWindowShortcut.ToggleHold when _viewModel.IsArmEnabled: _viewModel.IsHold = !_viewModel.IsHold; break;
            case MainWindowShortcut.LiveView: _viewModel.ResumeLiveCommand.Execute(null); break;
            case MainWindowShortcut.Reconnect: _viewModel.ReconnectCommand.Execute(null); break;
        }
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
