using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Updates;

public sealed class UpdateCoordinator : IDisposable
{
    private readonly IUpdateCheckService _updateCheckService;
    private readonly SettingsService _settingsService;
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly IExternalUriService _externalUriService;
    private readonly IUpdatePresentation _presentation;
    private readonly ReleaseVersion _currentVersion;
    private readonly DeploymentType _deploymentType;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _createdAtUtc;
    private readonly UpdateCoordinatorOptions _options;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _startupStarted;
    private int _manualRunning;
    private bool _disposed;

    public UpdateCoordinator(
        IUpdateCheckService updateCheckService,
        SettingsService settingsService,
        IDiagnosticLog diagnosticLog,
        IExternalUriService externalUriService,
        IUpdatePresentation presentation,
        ReleaseVersion currentVersion,
        DeploymentType deploymentType,
        TimeProvider? timeProvider = null,
        UpdateCoordinatorOptions? options = null)
    {
        _updateCheckService = updateCheckService ?? throw new ArgumentNullException(nameof(updateCheckService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
        _externalUriService = externalUriService ?? throw new ArgumentNullException(nameof(externalUriService));
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _deploymentType = deploymentType;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new UpdateCoordinatorOptions();
        _createdAtUtc = _timeProvider.GetUtcNow();
    }

    public async Task RunStartupCheckAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _startupStarted, 1) != 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (now - _createdAtUtc >= _options.StartupPresentationDeadline)
        {
            WriteSkipped("startup_deadline_elapsed");
            return;
        }

        var settings = _settingsService.Load();
        if (IsCooldownActive(settings.LastUpdateNotificationUtc, now, _options.NotificationCooldown))
        {
            WriteSkipped("notification_cooldown");
            return;
        }

        var result = await RunCheckAsync("startup", _options.StartupTimeout).ConfigureAwait(true);
        if (result is null || result.Status != UpdateCheckStatus.UpdateAvailable)
        {
            return;
        }

        if (_disposed
            || !_presentation.CanPresent
            || _timeProvider.GetUtcNow() - _createdAtUtc >= _options.StartupPresentationDeadline)
        {
            WriteSkipped("presentation_deadline_elapsed");
            return;
        }

        PresentUpdate("startup", result);
    }

    public async Task RunManualCheckAsync()
    {
        if (_disposed || Interlocked.CompareExchange(ref _manualRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var result = await RunCheckAsync("manual", _options.ManualTimeout).ConfigureAwait(true);
            if (result is null || _disposed || !_presentation.CanPresent)
            {
                return;
            }

            switch (result.Status)
            {
                case UpdateCheckStatus.Current:
                    _presentation.ShowCurrent($"CoH Analytics {_currentVersion.DisplayText} is up to date.");
                    break;
                case UpdateCheckStatus.UpdateAvailable:
                    PresentUpdate("manual", result);
                    break;
                case UpdateCheckStatus.UnableToCheck:
                    _presentation.ShowUnableToCheck(
                        "CoH Analytics couldn’t check for updates.\nCheck your internet connection and try again.");
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _manualRunning, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    internal static bool IsCooldownActive(
        DateTimeOffset? lastNotificationUtc,
        DateTimeOffset nowUtc,
        TimeSpan? cooldown = null)
    {
        if (lastNotificationUtc is null || lastNotificationUtc > nowUtc)
        {
            return false;
        }

        return nowUtc - lastNotificationUtc.Value < (cooldown ?? TimeSpan.FromHours(24));
    }

    private async Task<UpdateCheckResult?> RunCheckAsync(string mode, TimeSpan timeout)
    {
        _diagnosticLog.Write(new UpdateCheckStartedDiagnosticEvent
        {
            Mode = mode,
            LocalVersion = _currentVersion.CanonicalText,
            DeploymentType = _deploymentType.ToString()
        });

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        cancellation.CancelAfter(timeout);
        var result = await _updateCheckService.CheckAsync(cancellation.Token).ConfigureAwait(true);

        if (_disposed)
        {
            return null;
        }

        if (result.Status == UpdateCheckStatus.UnableToCheck)
        {
            var failureCode = cancellation.IsCancellationRequested && !_lifetimeCancellation.IsCancellationRequested
                ? "timeout"
                : result.FailureCode ?? "unknown_failure";
            _diagnosticLog.Write(new UpdateCheckFailedDiagnosticEvent
            {
                Mode = mode,
                FailureCode = failureCode,
                HttpStatus = result.HttpStatus,
                ExceptionType = result.ExceptionType,
                HResult = result.HResult
            });
            return result;
        }

        _diagnosticLog.Write(new UpdateCheckSucceededDiagnosticEvent
        {
            Mode = mode,
            ReleaseCount = result.ReleaseCount
        });

        if (result.Status == UpdateCheckStatus.Current)
        {
            _diagnosticLog.Write(new UpdateAlreadyCurrentDiagnosticEvent
            {
                Mode = mode,
                LocalVersion = _currentVersion.CanonicalText,
                RemoteVersion = result.RemoteVersion?.CanonicalText
            });
        }
        else
        {
            _diagnosticLog.Write(new UpdateAvailableDiagnosticEvent
            {
                Mode = mode,
                LocalVersion = _currentVersion.CanonicalText,
                RemoteVersion = result.RemoteVersion!.CanonicalText,
                DeploymentType = _deploymentType.ToString(),
                HasPackageDownload = result.DownloadAction is not null
            });
        }

        return result;
    }

    private void PresentUpdate(string mode, UpdateCheckResult result)
    {
        var remoteVersion = result.RemoteVersion!;
        PersistNotification(remoteVersion, _timeProvider.GetUtcNow());

        var sourceMessage = _deploymentType is DeploymentType.SourceBuild or DeploymentType.Unknown
            ? "This copy was built from source. CoH Analytics will not modify your source checkout."
            : null;
        var presentation = new UpdateAvailablePresentation(
            $"CoH Analytics {remoteVersion.DisplayText} is available.\nYou are running {_currentVersion.DisplayText}.",
            sourceMessage,
            sourceMessage is null ? "View Release Notes" : "View Release",
            result.DownloadAction?.Label);

        var action = _presentation.ShowUpdateAvailable(presentation);
        _diagnosticLog.Write(new UpdateUserActionSelectedDiagnosticEvent
        {
            Mode = mode,
            Action = action.ToString(),
            RemoteVersion = remoteVersion.CanonicalText,
            DeploymentType = _deploymentType.ToString()
        });

        switch (action)
        {
            case UpdateUserAction.Later:
                PersistNotification(remoteVersion, _timeProvider.GetUtcNow());
                break;
            case UpdateUserAction.ViewRelease:
                TryOpen(result.ReleaseUrl);
                break;
            case UpdateUserAction.DownloadPackage when result.DownloadAction is not null:
                TryOpen(result.DownloadAction.Url);
                break;
        }
    }

    private void PersistNotification(ReleaseVersion version, DateTimeOffset nowUtc)
    {
        _settingsService.Update(settings =>
        {
            settings.LastUpdateNotificationUtc = nowUtc;
            settings.LastNotifiedReleaseVersion = version.CanonicalText;
        });
    }

    private void TryOpen(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)
            || !_externalUriService.TryOpenUri(uri, out _))
        {
            _presentation.ShowUnableToOpenLink();
        }
    }

    private void WriteSkipped(string reason)
    {
        _diagnosticLog.Write(new UpdateCheckSkippedDiagnosticEvent
        {
            Mode = "startup",
            LocalVersion = _currentVersion.CanonicalText,
            DeploymentType = _deploymentType.ToString(),
            Reason = reason
        });
    }
}

public sealed record UpdateCoordinatorOptions
{
    public TimeSpan NotificationCooldown { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ManualTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan StartupPresentationDeadline { get; init; } = TimeSpan.FromSeconds(10);
}
