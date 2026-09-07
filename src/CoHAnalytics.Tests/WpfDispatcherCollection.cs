using System.Windows;
using System.Windows.Threading;

namespace CoHAnalytics.Tests;

/// <summary>
/// Serializes every test that touches the process-wide WPF <c>Application.Current</c> dispatcher.
/// </summary>
/// <remarks>
/// WPF permits only one <c>Application</c> per AppDomain, even after shutdown. The collection
/// fixture therefore owns one STA application and dispatcher for the complete collection lifetime.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfDispatcherCollection : ICollectionFixture<WpfDispatcherFixture>
{
    public const string Name = "WPF dispatcher";
}

public sealed class WpfDispatcherFixture : IDisposable
{
    private readonly Thread _uiThread;

    public WpfDispatcherFixture()
    {
        var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiThread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                started.TrySetResult(application.Dispatcher);
                application.Run();
            }
            catch (Exception exception)
            {
                started.TrySetException(exception);
            }
        })
        {
            IsBackground = true
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        Dispatcher = started.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }

    public Dispatcher Dispatcher { get; }

    public Task InvokeAsync(Action action) => Dispatcher.InvokeAsync(action).Task;

    public Task InvokeAsync(Func<Task> action) => Dispatcher.InvokeAsync(action).Task.Unwrap();

    public Task DrainAsync() =>
        Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background).Task;

    public void Dispose()
    {
        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            Dispatcher.Invoke(static () => Application.Current?.Shutdown());
        }

        if (!_uiThread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The shared WPF dispatcher did not shut down.");
        }
    }
}
