using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;
using CoHAnalytics.Updates;

namespace CoHAnalytics.Tests.Updates;

public sealed class UpdateCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_startup_notifies_once_and_persists_notification()
    {
        using var harness = new Harness(Now, Available());

        await harness.Coordinator.RunStartupCheckAsync();
        await harness.Coordinator.RunStartupCheckAsync();

        Assert.Equal(1, harness.CheckService.CallCount);
        Assert.Equal(1, harness.Presentation.UpdateAvailableCalls);
        var settings = harness.Settings.Load();
        Assert.Equal(Now, settings.LastUpdateNotificationUtc);
        Assert.Equal("0.1.3-beta", settings.LastNotifiedReleaseVersion);
    }

    [Theory]
    [InlineData(23, 59, 59, false)]
    [InlineData(24, 0, 0, true)]
    [InlineData(25, 0, 0, true)]
    public async Task Startup_cooldown_has_exact_24_hour_boundary(
        int hours,
        int minutes,
        int seconds,
        bool shouldCheck)
    {
        using var harness = new Harness(Now, Available());
        harness.Settings.Save(new AppSettings
        {
            LastUpdateNotificationUtc = Now - new TimeSpan(hours, minutes, seconds),
            LastNotifiedReleaseVersion = "0.1.2-beta"
        });

        await harness.Coordinator.RunStartupCheckAsync();

        Assert.Equal(shouldCheck ? 1 : 0, harness.CheckService.CallCount);
        Assert.Equal(shouldCheck ? 1 : 0, harness.Presentation.UpdateAvailableCalls);
    }

    [Fact]
    public async Task Newer_release_during_cooldown_is_suppressed_without_network_request()
    {
        using var harness = new Harness(Now, Available("0.9.0"));
        harness.Settings.Save(new AppSettings
        {
            LastUpdateNotificationUtc = Now - TimeSpan.FromHours(1),
            LastNotifiedReleaseVersion = "0.1.3-beta"
        });

        await harness.Coordinator.RunStartupCheckAsync();

        Assert.Equal(0, harness.CheckService.CallCount);
        Assert.Equal(0, harness.Presentation.UpdateAvailableCalls);
        Assert.Contains(
            harness.Diagnostics.Events,
            value => value is UpdateCheckSkippedDiagnosticEvent { Reason: "notification_cooldown" });
    }

    [Fact]
    public async Task Future_timestamp_fails_open_instead_of_suppressing_indefinitely()
    {
        using var harness = new Harness(Now, Available());
        harness.Settings.Save(new AppSettings
        {
            LastUpdateNotificationUtc = Now + TimeSpan.FromDays(30)
        });

        await harness.Coordinator.RunStartupCheckAsync();

        Assert.Equal(1, harness.CheckService.CallCount);
        Assert.Equal(1, harness.Presentation.UpdateAvailableCalls);
    }

    [Fact]
    public async Task Later_refreshes_notification_timestamp()
    {
        using var harness = new Harness(Now, Available());
        harness.Presentation.NextAction = UpdateUserAction.Later;
        harness.Presentation.OnShowUpdate = () => harness.Time.Advance(TimeSpan.FromMinutes(3));

        await harness.Coordinator.RunStartupCheckAsync();

        Assert.Equal(Now + TimeSpan.FromMinutes(3), harness.Settings.Load().LastUpdateNotificationUtc);
    }

    [Fact]
    public async Task Manual_check_bypasses_cooldown_and_shows_update()
    {
        using var harness = new Harness(Now, Available());
        harness.Settings.Save(new AppSettings
        {
            LastUpdateNotificationUtc = Now - TimeSpan.FromMinutes(5),
            LastNotifiedReleaseVersion = "0.1.3-beta"
        });

        await harness.Coordinator.RunManualCheckAsync();

        Assert.Equal(1, harness.CheckService.CallCount);
        Assert.Equal(1, harness.Presentation.UpdateAvailableCalls);
    }

    [Theory]
    [InlineData(UpdateCheckStatus.Current)]
    [InlineData(UpdateCheckStatus.UnableToCheck)]
    public async Task Manual_current_or_failure_shows_result_without_changing_cooldown(UpdateCheckStatus status)
    {
        var original = Now - TimeSpan.FromHours(3);
        using var harness = new Harness(Now, status == UpdateCheckStatus.Current ? Current() : Unable());
        harness.Settings.Save(new AppSettings
        {
            HomecomingInstallPath = "C:\\Games\\Homecoming",
            LastUpdateNotificationUtc = original,
            LastNotifiedReleaseVersion = "0.1.3-beta"
        });

        await harness.Coordinator.RunManualCheckAsync();

        Assert.Equal(status == UpdateCheckStatus.Current ? 1 : 0, harness.Presentation.CurrentCalls);
        Assert.Equal(status == UpdateCheckStatus.UnableToCheck ? 1 : 0, harness.Presentation.UnableCalls);
        var settings = harness.Settings.Load();
        Assert.Equal(original, settings.LastUpdateNotificationUtc);
        Assert.Equal("0.1.3-beta", settings.LastNotifiedReleaseVersion);
        Assert.Equal("C:\\Games\\Homecoming", settings.HomecomingInstallPath);
    }

    [Fact]
    public async Task Duplicate_manual_invocation_is_ignored_while_first_is_running()
    {
        var pending = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new Harness(Now, pending.Task);

        var first = harness.Coordinator.RunManualCheckAsync();
        await WaitUntilAsync(() => harness.CheckService.CallCount == 1);
        await harness.Coordinator.RunManualCheckAsync();

        Assert.Equal(1, harness.CheckService.CallCount);
        pending.SetResult(Current());
        await first;
        Assert.Equal(1, harness.Presentation.CurrentCalls);
    }

    [Fact]
    public async Task Startup_timeout_is_silent_and_does_not_present_late()
    {
        using var harness = new Harness(
            Now,
            cancellationAwareTimeout: true,
            options: new UpdateCoordinatorOptions
            {
                StartupTimeout = TimeSpan.FromMilliseconds(20),
                StartupPresentationDeadline = TimeSpan.FromSeconds(1)
            });

        await harness.Coordinator.RunStartupCheckAsync();

        Assert.Equal(0, harness.Presentation.TotalCalls);
        Assert.Contains(
            harness.Diagnostics.Events,
            value => value is UpdateCheckFailedDiagnosticEvent { Mode: "startup", FailureCode: "timeout" });
    }

    [Fact]
    public async Task Manual_timeout_shows_unable_to_check()
    {
        using var harness = new Harness(
            Now,
            cancellationAwareTimeout: true,
            options: new UpdateCoordinatorOptions { ManualTimeout = TimeSpan.FromMilliseconds(20) });

        await harness.Coordinator.RunManualCheckAsync();

        Assert.Equal(1, harness.Presentation.UnableCalls);
        Assert.Equal(0, harness.Presentation.UpdateAvailableCalls);
    }

    [Fact]
    public async Task Shutdown_cancels_session_and_prevents_delayed_presentation()
    {
        var pending = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new Harness(Now, pending.Task);

        var startup = harness.Coordinator.RunStartupCheckAsync();
        await WaitUntilAsync(() => harness.CheckService.CallCount == 1);
        harness.Coordinator.Dispose();
        pending.SetResult(Available());
        await startup;

        Assert.Equal(0, harness.Presentation.TotalCalls);
    }

    [Theory]
    [InlineData(UpdateUserAction.ViewRelease, "https://github.com/WITSLLC/CoH-Analytics/releases/tag/v0.1.3-beta")]
    [InlineData(UpdateUserAction.DownloadPackage, "https://github.com/WITSLLC/CoH-Analytics/releases/download/v0.1.3-beta/CoH-Analytics-0.1.3-beta-win-x64.msi")]
    public async Task Explicit_user_action_opens_validated_result_link(UpdateUserAction action, string expectedUri)
    {
        using var harness = new Harness(Now, Available(withDownload: true));
        harness.Presentation.NextAction = action;

        await harness.Coordinator.RunManualCheckAsync();

        Assert.Equal(expectedUri, harness.ExternalUri.LastOpenedUri);
    }

    [Fact]
    public async Task Source_build_presentation_has_no_download_and_explains_source_behavior()
    {
        using var harness = new Harness(Now, Available());

        await harness.Coordinator.RunManualCheckAsync();

        Assert.NotNull(harness.Presentation.LastUpdate);
        Assert.Contains("built from source", harness.Presentation.LastUpdate!.SourceBuildMessage, StringComparison.Ordinal);
        Assert.Null(harness.Presentation.LastUpdate.DownloadActionLabel);
        Assert.Equal("View Release", harness.Presentation.LastUpdate.ReleaseActionLabel);
    }

    private static UpdateCheckResult Available(string version = "0.1.3-beta", bool withDownload = false)
    {
        var remote = Parse(version);
        return new UpdateCheckResult
        {
            Status = UpdateCheckStatus.UpdateAvailable,
            CurrentVersion = Parse("0.1.2-beta"),
            RemoteVersion = remote,
            DeploymentType = DeploymentType.SourceBuild,
            ReleaseUrl = $"https://github.com/WITSLLC/CoH-Analytics/releases/tag/{remote.TagText}",
            DownloadAction = withDownload
                ? new UpdateDownloadAction(
                    "Download Installer",
                    "CoH-Analytics-0.1.3-beta-win-x64.msi",
                    "https://github.com/WITSLLC/CoH-Analytics/releases/download/v0.1.3-beta/CoH-Analytics-0.1.3-beta-win-x64.msi")
                : null,
            ReleaseCount = 1
        };
    }

    private static UpdateCheckResult Current() => new()
    {
        Status = UpdateCheckStatus.Current,
        CurrentVersion = Parse("0.1.2-beta"),
        RemoteVersion = Parse("0.1.2-beta"),
        DeploymentType = DeploymentType.SourceBuild,
        ReleaseCount = 1
    };

    private static UpdateCheckResult Unable() => new()
    {
        Status = UpdateCheckStatus.UnableToCheck,
        CurrentVersion = Parse("0.1.2-beta"),
        DeploymentType = DeploymentType.SourceBuild,
        FailureCode = "request_failed"
    };

    private static ReleaseVersion Parse(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var result));
        return result!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeoutAt = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeoutAt)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Yield();
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private bool _disposed;

        public Harness(
            DateTimeOffset now,
            UpdateCheckResult result,
            UpdateCoordinatorOptions? options = null)
            : this(now, Task.FromResult(result), options)
        {
        }

        public Harness(
            DateTimeOffset now,
            Task<UpdateCheckResult> result,
            UpdateCoordinatorOptions? options = null)
        {
            Time = new ManualTimeProvider(now);
            CheckService = new StubUpdateCheckService(_ => result);
            Settings = new SettingsService(_directory);
            Diagnostics = new RecordingDiagnosticLog();
            Presentation = new RecordingPresentation();
            ExternalUri = new RecordingExternalUriService();
            Coordinator = new UpdateCoordinator(
                CheckService,
                Settings,
                Diagnostics,
                ExternalUri,
                Presentation,
                Parse("0.1.2-beta"),
                DeploymentType.SourceBuild,
                Time,
                options);
        }

        public Harness(
            DateTimeOffset now,
            bool cancellationAwareTimeout,
            UpdateCoordinatorOptions options)
        {
            Assert.True(cancellationAwareTimeout);
            Time = new ManualTimeProvider(now);
            CheckService = new StubUpdateCheckService(async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Delay unexpectedly completed.");
                }
                catch (OperationCanceledException exception)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.UnableToCheck,
                        CurrentVersion = Parse("0.1.2-beta"),
                        DeploymentType = DeploymentType.SourceBuild,
                        FailureCode = "cancelled",
                        ExceptionType = exception.GetType().FullName,
                        HResult = exception.HResult
                    };
                }
            });
            Settings = new SettingsService(_directory);
            Diagnostics = new RecordingDiagnosticLog();
            Presentation = new RecordingPresentation();
            ExternalUri = new RecordingExternalUriService();
            Coordinator = new UpdateCoordinator(
                CheckService,
                Settings,
                Diagnostics,
                ExternalUri,
                Presentation,
                Parse("0.1.2-beta"),
                DeploymentType.SourceBuild,
                Time,
                options);
        }

        public ManualTimeProvider Time { get; }
        public StubUpdateCheckService CheckService { get; }
        public SettingsService Settings { get; }
        public RecordingDiagnosticLog Diagnostics { get; }
        public RecordingPresentation Presentation { get; }
        public RecordingExternalUriService ExternalUri { get; }
        public UpdateCoordinator Coordinator { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Coordinator.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class StubUpdateCheckService(Func<CancellationToken, Task<UpdateCheckResult>> check)
        : IUpdateCheckService
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return check(cancellationToken);
        }
    }

    private sealed class RecordingPresentation : IUpdatePresentation
    {
        public bool CanPresent { get; set; } = true;
        public UpdateUserAction NextAction { get; set; } = UpdateUserAction.Later;
        public Action? OnShowUpdate { get; set; }
        public int UpdateAvailableCalls { get; private set; }
        public int CurrentCalls { get; private set; }
        public int UnableCalls { get; private set; }
        public int UnableToOpenCalls { get; private set; }
        public int TotalCalls => UpdateAvailableCalls + CurrentCalls + UnableCalls + UnableToOpenCalls;
        public UpdateAvailablePresentation? LastUpdate { get; private set; }

        public UpdateUserAction ShowUpdateAvailable(UpdateAvailablePresentation presentation)
        {
            UpdateAvailableCalls++;
            LastUpdate = presentation;
            OnShowUpdate?.Invoke();
            return NextAction;
        }

        public void ShowCurrent(string message) => CurrentCalls++;
        public void ShowUnableToCheck(string message) => UnableCalls++;
        public void ShowUnableToOpenLink() => UnableToOpenCalls++;
    }

    private sealed class RecordingExternalUriService : IExternalUriService
    {
        public string? LastOpenedUri { get; private set; }

        public bool TryOpenUri(string uri, out string? failureReason)
        {
            LastOpenedUri = uri;
            failureReason = null;
            return true;
        }
    }

    private sealed class RecordingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = [];
        public bool IsEnabled(DiagnosticChannel channel, DiagnosticCategory category) => true;
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);

        public DiagnosticLogStatus GetStatus() => new()
        {
            StreamState = DiagnosticLogStreamState.Active,
            ActivePath = string.Empty,
            ApplicationRunId = Guid.Empty,
            QueueDepth = 0,
            PeakQueueDepth = 0,
            WrittenCount = Events.Count,
            DroppedCount = 0
        };
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
