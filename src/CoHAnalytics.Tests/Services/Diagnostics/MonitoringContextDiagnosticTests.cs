using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services.Diagnostics;

public sealed class MonitoringContextDiagnosticTests
{
    private static readonly DateTimeOffset StartA = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = StartA.AddMinutes(1);

    [Fact]
    public async Task Context_creation_emits_created_then_enriched_snapshot()
    {
        var log = new RecordingDiagnosticLog();
        var (manager, _, _, _) = await CreateStartedManager([], log);

        var contextId = manager.AddContext();

        Assert.IsType<MonitoringContextCreatedDiagnosticEvent>(log.Events[0]);
        var created = (MonitoringContextCreatedDiagnosticEvent)log.Events[0];
        Assert.Equal(contextId.ToString(), created.ContextId);
        Assert.Equal(MonitoringContextState.WaitingForSource, created.State);
        Assert.IsType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>(log.Events[^1]);
        Assert.Equal(
            "MonitoringContextCreated",
            ((DiagnosticsStateSnapshotCapturedDiagnosticEvent)log.Events[^1]).Reason);
    }

    [Fact]
    public async Task State_transition_emits_once_and_unchanged_state_does_not_repeat()
    {
        var log = new RecordingDiagnosticLog();
        var (manager, runtime, _, _) = await CreateStartedManager([], log);
        manager.AddContext();
        log.Clear();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        var changed = Assert.Single(log.Events.OfType<MonitoringContextStateChangedDiagnosticEvent>());
        Assert.Equal(MonitoringContextState.WaitingForSource, changed.PreviousState);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, changed.NextState);
        log.Clear();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Error, []);

        Assert.Empty(log.Events.OfType<MonitoringContextStateChangedDiagnosticEvent>());
    }

    [Fact]
    public async Task Source_and_account_binding_emit_once_without_unchanged_duplicates()
    {
        var log = new RecordingDiagnosticLog();
        var source = TestLogCandidates.SourceId("stable-account", "Private Account");
        var candidate = TestLogCandidates.Create(source, LogSourceActivityState.Waiting, StartA);
        var (manager, _, logActivity, _) = await CreateStartedManager([], log, candidate);
        var contextId = manager.AddContext();
        log.Clear();

        var result = manager.ClaimSource(contextId, source);

        Assert.Equal(MonitoringSourceClaimOutcome.Success, result.Outcome);
        var sourceChanged = Assert.Single(log.Events.OfType<MonitoringContextSourceBindingChangedDiagnosticEvent>());
        Assert.Null(sourceChanged.PreviousSourceId);
        Assert.Equal(source.Value, sourceChanged.NextSourceId);
        Assert.Equal(source.FileName, sourceChanged.NextSourceFileName);
        var accountBound = Assert.Single(log.Events.OfType<MonitoringContextAccountBoundDiagnosticEvent>());
        Assert.Null(accountBound.PreviousAccountStableId);
        Assert.Equal(source.AccountStableId, accountBound.NextAccountStableId);
        log.Clear();

        logActivity.RaiseActivityChanged();

        Assert.Empty(log.Events.OfType<MonitoringContextSourceBindingChangedDiagnosticEvent>());
        Assert.Empty(log.Events.OfType<MonitoringContextAccountBoundDiagnosticEvent>());
    }

    [Fact]
    public async Task Actual_process_association_emits_changed_event()
    {
        var log = new RecordingDiagnosticLog();
        var client = Client(3524, StartA);
        var (manager, _, _, _) = await CreateStartedManager([client], log);

        var contextId = manager.AddContext();

        var changed = Assert.Single(log.Events.OfType<MonitoringContextProcessBindingChangedDiagnosticEvent>());
        Assert.Equal(contextId.ToString(), changed.ContextId);
        Assert.Null(changed.PreviousProcess);
        Assert.Equal(client.ProcessId, changed.NextProcess!.ProcessId);
        Assert.Equal(client.ProcessStartTime, changed.NextProcess.ProcessStartTime);
    }

    [Fact]
    public async Task Two_client_contexts_emit_binding_unavailable_once_each()
    {
        var log = new RecordingDiagnosticLog();
        var sources = new[]
        {
            TestLogCandidates.SourceId("TestAccount", "Private Alpha"),
            TestLogCandidates.SourceId("AltAccount", "Private Alt")
        };
        var (manager, _, logActivity, time) = await CreateStartedManager(
            [Client(3524, StartA), Client(6356, StartB)],
            log);
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(sources[0], LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(sources[1], LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        Assert.Equal(2, manager.Current.Contexts.Count);
        Assert.All(manager.Current.Contexts, context => Assert.Null(context.ProcessInstance));
        var unavailable = log.Events.OfType<MonitoringContextProcessBindingUnavailableDiagnosticEvent>().ToArray();
        Assert.Equal(2, unavailable.Length);
        Assert.All(
            unavailable,
            item => Assert.Equal(MonitoringProcessBindingUnavailableReason.MultipleRuntimeClients, item.Reason));
        log.Clear();

        logActivity.RaiseActivityChanged();

        Assert.Empty(log.Events.OfType<MonitoringContextProcessBindingUnavailableDiagnosticEvent>());
    }

    [Fact]
    public async Task Two_to_one_transition_records_current_process_association_behavior()
    {
        var log = new RecordingDiagnosticLog();
        var clientA = Client(3524, StartA);
        var clientB = Client(6356, StartB);
        var sourceA = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var sourceB = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        var (manager, runtime, logActivity, time) = await CreateStartedManager([clientA, clientB], log);
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(sourceA, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(sourceB, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();
        log.Clear();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [clientA]);

        var active = manager.Current.Contexts
            .Where(context => context.State != MonitoringContextState.Stopped)
            .ToArray();
        Assert.Equal(2, active.Length);
        Assert.All(active, context => Assert.Equal(clientA, context.ProcessInstance));
        var bindings = log.Events.OfType<MonitoringContextProcessBindingChangedDiagnosticEvent>().ToArray();
        Assert.Equal(2, bindings.Length);
        Assert.All(bindings, binding => Assert.Equal(clientA.ProcessId, binding.NextProcess!.ProcessId));
        Assert.Equal(
            "MonitoringProcessAssociationChanged",
            log.Events.OfType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>().Last().Reason);
    }

    [Fact]
    public async Task Creation_snapshot_contains_safe_runtime_source_and_context_state()
    {
        const string displayName = "Private Account Name";
        const string absolutePath = @"C:\private\accounts\secret\logs\chatlog 2026-09-02.txt";
        var log = new RecordingDiagnosticLog();
        var source = LogSourceId.Create("stable-account", displayName, absolutePath, new DateOnly(2026, 9, 2));
        var candidate = TestLogCandidates.Create(source, LogSourceActivityState.Waiting, StartA);
        var clients = new[] { Client(3524, StartA), Client(6356, StartB) };
        var (manager, _, _, _) = await CreateStartedManager(clients, log, candidate);
        log.Clear();

        var contextId = manager.AddContext("stable-account", displayName, source);

        var snapshot = Assert.Single(log.Events.OfType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>());
        Assert.Equal(2, snapshot.RunningClients.Count);
        Assert.Equal(source.Value, Assert.Single(snapshot.LogSources).SourceId);
        var context = Assert.Single(snapshot.MonitoringContexts);
        Assert.Equal(contextId.ToString(), context.ContextId);
        Assert.Equal(source.Value, context.SourceId);
        Assert.Null(context.ProcessInstance);
        Assert.Equal(1, context.BindingGeneration);
        var serialized = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain(absolutePath, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(displayName, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("filePath", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountDisplayName", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executablePath", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Genuine_context_removal_emits_retired_then_snapshot()
    {
        var log = new RecordingDiagnosticLog();
        var (manager, _, _, _) = await CreateStartedManager([], log);
        var contextId = manager.AddContext();
        log.Clear();

        manager.ResetForNewRuntimeGeneration();

        var retired = Assert.Single(log.Events.OfType<MonitoringContextRetiredDiagnosticEvent>());
        Assert.Equal(contextId.ToString(), retired.ContextId);
        Assert.IsType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>(log.Events[^1]);
        Assert.Equal(
            "MonitoringContextRetired",
            ((DiagnosticsStateSnapshotCapturedDiagnosticEvent)log.Events[^1]).Reason);
    }

    [Fact]
    public async Task Throwing_diagnostic_logger_does_not_change_context_reconciliation()
    {
        var source = TestLogCandidates.SourceId("stable-account", "Private Account");
        var client = Client(3524, StartA);
        var (manager, _, logActivity, time) = await CreateStartedManager([client], new ThrowingDiagnosticLog());

        var createException = Record.Exception(() => manager.AddContext());
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, time.GetUtcNow()));
        var reconcileException = Record.Exception(logActivity.RaiseActivityChanged);

        Assert.Null(createException);
        Assert.Null(reconcileException);
        Assert.Contains(manager.Current.Contexts, context => context.AccountStableId == source.AccountStableId);
        Assert.All(
            manager.Current.Contexts.Where(context => context.State != MonitoringContextState.Stopped),
            context => Assert.Equal(client, context.ProcessInstance));
    }

    private static HomecomingProcessInstance Client(int processId, DateTimeOffset startTime) =>
        FakeGameRuntimeService.CreateClient(processId, startTime, @"C:\private\cityofheroes.exe");

    private static async Task<(
        MonitoringSessionManager Manager,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time)> CreateStartedManager(
        IReadOnlyList<HomecomingProcessInstance> clients,
        IDiagnosticLog diagnosticLog,
        params LogSourceCandidate[] candidates)
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClients = clients,
            RunningClientCount = clients.Count
        };
        var time = new ManualTimeProvider(StartA);
        var logActivity = new FakeLogActivityService
        {
            Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidates)
        };
        var manager = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time },
            diagnosticLog);
        await manager.StartAsync();
        return (manager, runtime, logActivity, time);
    }
}
