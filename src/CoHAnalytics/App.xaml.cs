using System.Windows;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics;

public partial class App : Application
{
    public AppServices Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        Services = AppServices.Create();

        var mainWindow = new MainWindow(Services);
        MainWindow = mainWindow;
        mainWindow.Show();

        var startupSucceeded = true;
        try
        {
            await Services.InitializeOrchestratorAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            startupSucceeded = false;
            Services.DiagnosticLog.Write(new ApplicationStartupFailedDiagnosticEvent
            {
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                HResult = exception.HResult,
                FailureCode = "orchestrator_start_failed"
            });
            // Contributor failures are represented in orchestrator state rather than crashing startup.
        }

        base.OnStartup(e);
        if (startupSucceeded)
        {
            Services.DiagnosticLog.Write(new ApplicationStartupCompletedDiagnosticEvent());
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.DiagnosticLog.Write(new ApplicationShutdownStartedDiagnosticEvent
        {
            ExitCode = e.ApplicationExitCode
        });
        DisposeMainWindowViewModel();
        Services.Dispose();
        base.OnExit(e);
    }

    internal void DisposeMainWindowViewModel()
    {
        if (MainWindow?.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
