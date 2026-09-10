using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services.Diagnostics;

public sealed class RuntimeDiagnosticTests
{
    private static readonly DateTimeOffset StartA = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = StartA.AddMinutes(1);

    [Fact]
    public void Single_client_detection_emits_topology_status_and_snapshot_in_order()
    {
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log);
        var client = Client(101, StartA);

        service.ApplyStatus(GameRuntimeStatus.Running, [client]);

        Assert.Collection(
            log.Events,
            item => Assert.IsType<RuntimeClientSetChangedDiagnosticEvent>(item),
            item => Assert.IsType<RuntimeStatusChangedDiagnosticEvent>(item),
            item => Assert.IsType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>(item));
        var topology = Assert.IsType<RuntimeClientSetChangedDiagnosticEvent>(log.Events[0]);
        Assert.Empty(topology.PreviousClients);
        var observed = Assert.Single(topology.NextClients);
        Assert.Equal(client.ProcessId, observed.ProcessId);
        Assert.Equal(client.ProcessStartTime, observed.ProcessStartTime);
    }

    [Fact]
    public void One_to_two_and_two_to_one_each_emit_client_set_change()
    {
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log);
        var first = Client(101, StartA);
        var second = Client(202, StartB);
        service.ApplyStatus(GameRuntimeStatus.Running, [first]);
        log.Clear();

        service.ApplyStatus(GameRuntimeStatus.Running, [first, second]);
        var expanded = Assert.Single(log.Events.OfType<RuntimeClientSetChangedDiagnosticEvent>());
        Assert.Single(expanded.PreviousClients);
        Assert.Equal(2, expanded.NextClients.Count);

        log.Clear();
        service.ApplyStatus(GameRuntimeStatus.Running, [second]);
        var contracted = Assert.Single(log.Events.OfType<RuntimeClientSetChangedDiagnosticEvent>());
        Assert.Equal(2, contracted.PreviousClients.Count);
        Assert.Single(contracted.NextClients);
    }

    [Fact]
    public void Unchanged_two_client_set_emits_nothing()
    {
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log);
        var clients = new[] { Client(101, StartA), Client(202, StartB) };
        service.ApplyStatus(GameRuntimeStatus.Running, clients);
        log.Clear();

        service.ApplyStatus(GameRuntimeStatus.Running, clients);

        Assert.Empty(log.Events);
    }

    [Fact]
    public void Two_client_replacement_with_unchanged_count_is_detected()
    {
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log);
        var retained = Client(101, StartA);
        var removed = Client(202, StartA);
        var added = Client(303, StartB);
        service.ApplyStatus(GameRuntimeStatus.Running, [retained, removed]);
        log.Clear();

        service.ApplyStatus(GameRuntimeStatus.Running, [retained, added]);

        var changed = Assert.Single(log.Events.OfType<RuntimeClientSetChangedDiagnosticEvent>());
        Assert.Equal([101, 202], changed.PreviousClients.Select(client => client.ProcessId));
        Assert.Equal([101, 303], changed.NextClients.Select(client => client.ProcessId));
        Assert.Single(log.Events.OfType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>());
    }

    [Fact]
    public void Same_pid_with_different_start_time_is_a_different_client_identity()
    {
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log);
        service.ApplyStatus(GameRuntimeStatus.Running, [Client(101, StartA)]);
        log.Clear();

        service.ApplyStatus(GameRuntimeStatus.Running, [Client(101, StartB)]);

        var changed = Assert.Single(log.Events.OfType<RuntimeClientSetChangedDiagnosticEvent>());
        Assert.Equal(StartA, Assert.Single(changed.PreviousClients).ProcessStartTime);
        Assert.Equal(StartB, Assert.Single(changed.NextClients).ProcessStartTime);
    }

    [Fact]
    public void Status_only_transition_emits_runtime_status_change_without_snapshot()
    {
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log);

        service.ApplyStatus(GameRuntimeStatus.Off, []);

        var changed = Assert.Single(log.Events);
        var status = Assert.IsType<RuntimeStatusChangedDiagnosticEvent>(changed);
        Assert.Equal(GameRuntimeStatus.Unconfigured, status.PreviousStatus);
        Assert.Equal(GameRuntimeStatus.Off, status.NextStatus);
    }

    [Fact]
    public void Client_set_snapshot_contains_current_clients_and_privacy_safe_log_sources()
    {
        const string displayName = "Private Account Name";
        var absolutePath = Path.Combine(Path.GetTempPath(), "private-account", "Logs", "chatlog 2026-09-02.txt");
        var sourceId = LogSourceId.Create("stable-account-id", displayName, absolutePath, new DateOnly(2026, 9, 2));
        var snapshot = LogActivitySnapshot.Create(
            [Source(sourceId)],
            observedAccountCount: 1,
            logsFolderCount: 1,
            observedAt: StartA,
            revision: 1);
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log, () => snapshot);

        service.ApplyStatus(GameRuntimeStatus.Running, [Client(101, StartA)]);

        var diagnosticSnapshot = Assert.Single(log.Events.OfType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>());
        Assert.Equal("RuntimeClientSetChanged", diagnosticSnapshot.Reason);
        Assert.Equal(101, Assert.Single(diagnosticSnapshot.RunningClients).ProcessId);
        var source = Assert.Single(diagnosticSnapshot.LogSources);
        Assert.Equal(sourceId.Value, source.SourceId);
        Assert.Equal("stable-account-id", source.AccountStableId);
        Assert.Equal(sourceId.FileName, source.SourceFileName);
        var serialized = JsonSerializer.Serialize(diagnosticSnapshot);
        Assert.DoesNotContain(displayName, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(absolutePath, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("filePath", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("displayName", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Throwing_diagnostic_logger_does_not_change_runtime_behavior()
    {
        using var service = CreateRuntime(new ThrowingDiagnosticLog());
        var client = Client(101, StartA);

        var exception = Record.Exception(() => service.ApplyStatus(GameRuntimeStatus.Running, [client]));

        Assert.Null(exception);
        Assert.Equal(GameRuntimeStatus.Running, service.CurrentStatus);
        Assert.Equal(client, Assert.Single(service.RunningClients));
    }

    [Fact]
    public void StatusChanged_isolates_subscriber_faults_and_continues_dispatch()
    {
        var log = new RecordingDiagnosticLog();
        using var service = CreateRuntime(log);
        var firstCalled = false;
        var secondCalled = false;
        service.StatusChanged += (_, _) =>
        {
            firstCalled = true;
            throw new InvalidOperationException("subscriber fault");
        };
        service.StatusChanged += (_, _) => secondCalled = true;

        var exception = Record.Exception(
            () => service.ApplyStatus(GameRuntimeStatus.Running, [Client(101, StartA)]));

        Assert.Null(exception);
        Assert.True(firstCalled);
        Assert.True(secondCalled);
        var fault = Assert.Single(log.Events.OfType<EventSubscriberDispatchFailedDiagnosticEvent>());
        Assert.Equal("Runtime.StatusChanged", fault.EventSource);
        Assert.Equal(typeof(InvalidOperationException).FullName, fault.ExceptionType);
        Assert.False(string.IsNullOrWhiteSpace(fault.SubscriberId));
    }

    private static HomecomingRuntimeService CreateRuntime(
        IDiagnosticLog diagnosticLog,
        Func<LogActivitySnapshot>? logActivitySnapshotProvider = null)
    {
        var settings = new SettingsService();
        var installation = new HomecomingInstallationService(settings);
        return new HomecomingRuntimeService(
            installation,
            new HomecomingLauncherService(settings, installation),
            diagnosticLog,
            logActivitySnapshotProvider);
    }

    private static HomecomingProcessInstance Client(int processId, DateTimeOffset startTime) => new()
    {
        ProcessId = processId,
        ProcessStartTime = startTime,
        ExecutablePath = @"C:\private\homecoming.exe"
    };

    private static LogSourceCandidate Source(LogSourceId sourceId) => new()
    {
        SourceId = sourceId,
        AccountStableId = sourceId.AccountStableId,
        AccountDisplayName = sourceId.AccountDisplayName,
        FilePath = sourceId.FilePath,
        FileName = sourceId.FileName,
        LogDate = sourceId.LogDate,
        Exists = true,
        Length = 42,
        PreviousLength = 20,
        FirstObservedAt = StartA,
        LastObservedAt = StartA,
        FirstGrowthAt = StartA,
        LastGrowthAt = StartA,
        ActivityState = LogSourceActivityState.Growing,
        LastChangeKind = LogSourceChangeKind.Grew,
        IsCurrentDailyFile = true
    };
}

public sealed class LogActivityDiagnosticTests
{
    [Fact]
    public async Task New_source_emits_privacy_safe_source_discovered()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Private Display Name");
        var path = environment.WriteLog(account, environment.Today, "existing content");
        var log = new RecordingDiagnosticLog();
        using var service = CreateLogActivity(environment, log);

        await service.StartAsync();

        var discovered = Assert.Single(log.Events.OfType<LogActivitySourceDiscoveredDiagnosticEvent>());
        Assert.Equal(account.StableId, discovered.AccountStableId);
        Assert.Equal(Path.GetFileName(path), discovered.SourceFileName);
        Assert.Equal(LogSourceActivityState.Waiting, discovered.ActivityState);
        var serialized = JsonSerializer.Serialize(discovered);
        Assert.DoesNotContain(account.DisplayName, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(path, serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Waiting_to_growing_and_growing_to_inactive_emit_only_semantic_state_changes()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "start");
        var log = new RecordingDiagnosticLog();
        using var service = CreateLogActivity(environment, log, TimeSpan.FromSeconds(10));
        await service.StartAsync();
        log.Clear();

        environment.Append(account, environment.Today, "growth");
        await service.ScanAsync();
        var growing = Assert.Single(log.Events.OfType<LogActivitySourceStateChangedDiagnosticEvent>());
        Assert.Equal(LogSourceActivityState.Waiting, growing.PreviousActivityState);
        Assert.Equal(LogSourceActivityState.Growing, growing.NextActivityState);
        Assert.Equal("ObservedLengthIncrease", growing.Reason);

        log.Clear();
        await service.ScanAsync();
        Assert.Empty(log.Events);

        environment.Time.Advance(TimeSpan.FromSeconds(11));
        await service.ScanAsync();
        var inactive = Assert.Single(log.Events.OfType<LogActivitySourceStateChangedDiagnosticEvent>());
        Assert.Equal(LogSourceActivityState.Growing, inactive.PreviousActivityState);
        Assert.Equal(LogSourceActivityState.Inactive, inactive.NextActivityState);
        Assert.Equal("InactivityThresholdElapsed", inactive.Reason);

        log.Clear();
        environment.Time.Advance(TimeSpan.FromSeconds(1));
        await service.ScanAsync();
        Assert.Empty(log.Events);
    }

    [Fact]
    public async Task Disappearance_and_reappearance_emit_lost_then_replaced()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "original");
        var log = new RecordingDiagnosticLog();
        using var service = CreateLogActivity(environment, log);
        await service.StartAsync();
        var originalSourceId = service.Current.Candidates.Single().SourceId.Value;
        log.Clear();

        environment.Delete(account, environment.Today);
        await service.ScanAsync();
        var lost = Assert.Single(log.Events.OfType<LogActivitySourceLostDiagnosticEvent>());
        Assert.Equal(originalSourceId, lost.SourceId);
        Assert.Equal(LogSourceActivityState.Unavailable, lost.NextActivityState);

        log.Clear();
        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.WriteLog(account, environment.Today, "replacement");
        await service.ScanAsync();
        var replaced = Assert.Single(log.Events.OfType<LogActivitySourceReplacedDiagnosticEvent>());
        Assert.Equal(originalSourceId, replaced.PreviousSourceId);
        Assert.NotEqual(replaced.PreviousSourceId, replaced.SourceId);
        Assert.Equal("ObservedFileReplacement", replaced.Reason);
        Assert.NotNull(replaced.PreviousCreationTime);
        Assert.NotNull(replaced.CreationTime);
    }

    [Fact]
    public async Task Daily_rollover_candidate_emits_one_rollover_event()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        var yesterday = environment.Today.AddDays(-1);
        environment.WriteLog(account, yesterday, "yesterday");
        var log = new RecordingDiagnosticLog();
        using var service = CreateLogActivity(environment, log, TimeSpan.FromSeconds(10));
        await service.StartAsync();
        environment.Append(account, yesterday, "growth");
        await service.ScanAsync();
        environment.Time.Advance(TimeSpan.FromSeconds(30));
        environment.WriteLog(account, environment.Today);
        await service.ScanAsync();
        log.Clear();

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today, "growth");
        await service.ScanAsync();

        var rollover = Assert.Single(log.Events.OfType<LogActivitySourceRolloverDiagnosticEvent>());
        Assert.Equal(
            service.Current.Candidates.Single(candidate => candidate.LogDate == yesterday).SourceId.Value,
            rollover.PredecessorSourceId);
        Assert.Equal("DailyRolloverCandidate", rollover.Reason);

        log.Clear();
        await service.ScanAsync();
        Assert.Empty(log.Events);
    }

    [Fact]
    public async Task Throwing_diagnostic_logger_does_not_change_log_activity_behavior()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "start");
        using var service = CreateLogActivity(environment, new ThrowingDiagnosticLog());

        var startException = await Record.ExceptionAsync(() => service.StartAsync());
        environment.Append(account, environment.Today, "growth");
        var scanException = await Record.ExceptionAsync(() => service.ScanAsync());

        Assert.Null(startException);
        Assert.Null(scanException);
        Assert.Equal(LogSourceActivityState.Growing, Assert.Single(service.Current.Candidates).ActivityState);
    }

    [Fact]
    public async Task ActivityChanged_isolates_subscriber_faults_and_continues_dispatch()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "start");
        var log = new RecordingDiagnosticLog();
        using var service = CreateLogActivity(environment, log);
        var firstCalled = false;
        var secondCalled = false;
        service.ActivityChanged += (_, _) =>
        {
            firstCalled = true;
            throw new InvalidOperationException("subscriber fault");
        };
        service.ActivityChanged += (_, _) => secondCalled = true;

        await service.StartAsync();
        log.Clear();
        environment.Append(account, environment.Today, "growth");
        var exception = await Record.ExceptionAsync(() => service.ScanAsync());

        Assert.Null(exception);
        Assert.True(firstCalled);
        Assert.True(secondCalled);
        var fault = Assert.Single(log.Events.OfType<EventSubscriberDispatchFailedDiagnosticEvent>());
        Assert.Equal("LogActivity.ActivityChanged", fault.EventSource);
        Assert.Equal(typeof(InvalidOperationException).FullName, fault.ExceptionType);
    }

    private static LogActivityService CreateLogActivity(
        LogActivityTestEnvironment environment,
        IDiagnosticLog diagnosticLog,
        TimeSpan? inactivityThreshold = null) =>
        new(
            () => environment.AccountsProvider(),
            new LogActivityServiceOptions
            {
                TimeProvider = environment.Time,
                EnablePolling = false,
                InactivityThreshold = inactivityThreshold ?? TimeSpan.FromSeconds(30)
            },
            diagnosticLog);
}

internal sealed class RecordingDiagnosticLog : IDiagnosticLog
{
    private readonly List<DiagnosticEvent> _events = [];

    public IReadOnlyList<DiagnosticEvent> Events => _events;

    public bool IsEnabled(DiagnosticChannel channel, DiagnosticCategory category) => true;

    public void Write(DiagnosticEvent diagnosticEvent) => _events.Add(diagnosticEvent);

    public DiagnosticLogStatus GetStatus() => new()
    {
        StreamState = DiagnosticLogStreamState.Active,
        ActivePath = string.Empty,
        ApplicationRunId = Guid.Empty,
        QueueDepth = 0,
        PeakQueueDepth = 0,
        WrittenCount = _events.Count,
        DroppedCount = 0
    };

    public void Clear() => _events.Clear();
}

internal sealed class ThrowingDiagnosticLog : IDiagnosticLog
{
    public bool IsEnabled(DiagnosticChannel channel, DiagnosticCategory category) => throw new IOException("Injected failure.");

    public void Write(DiagnosticEvent diagnosticEvent) => throw new IOException("Injected failure.");

    public DiagnosticLogStatus GetStatus() => throw new IOException("Injected failure.");
}
