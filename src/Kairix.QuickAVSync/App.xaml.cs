using System.Configuration;
using System.Data;
using System.Windows;

namespace Kairix.QuickAVSync;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly Kairix.QuickAVSync.Windows.Infrastructure.AppLogger _startupLog = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            window.Show();
            window.Activate();
            _startupLog.Write("startup", "Main window created, shown, and activated.");
        }
        catch (Exception ex)
        {
            _startupLog.Write("startup.failure", ex.ToString());
            MessageBox.Show($"Kairix Quick A/V Sync could not start.\n\n{ex.Message}\n\nDiagnostic log: {_startupLog.Path}", "Kairix Quick A/V Sync", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _startupLog.Write("unhandled", e.Exception.ToString());
        MessageBox.Show($"An unexpected error occurred.\n\n{e.Exception.Message}\n\nDiagnostic log: {_startupLog.Path}", "Kairix Quick A/V Sync", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

