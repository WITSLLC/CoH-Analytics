using System.IO;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Services;

[Collection(WpfDispatcherCollection.Name)]
public sealed class ManualCharacterConfirmationDeadlockTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public ManualCharacterConfirmationDeadlockTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public async Task ConfirmCharacter_on_ui_dispatcher_completes_with_live_session_subscriber()
    {
        await _dispatcher
            .InvokeAsync(RunUiDispatcherConfirmScenarioAsync)
            .WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Manual_confirm_commits_retained_telemetry_exactly_once()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var committed = new List<GameplaySessionEvent>();

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var options = new GameplaySessionOptions { MaxRetainedEventCount = 4 };
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);
            manager.CommittedEventsAvailable += (_, args) => committed.AddRange(args.Events);

            var record = repository.EstablishTrustedFromWelcome("acct-1", "Known Hero");

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 structurally timestamped",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:02 You gain 100 experience.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:03 You gain 50 experience.",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.RetainedEventCount == 3));

            Assert.Empty(committed);

            var confirm = manager.ConfirmCharacter(contextId, record.RecordId!);
            Assert.True(confirm.IsSuccess);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => committed.Count == 3);

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
            Assert.Equal(record.RecordId, session.CharacterRecordId);
            Assert.Equal(150, session.SessionExperienceGained);
            Assert.Equal(0, session.RetainedEventCount);
            Assert.Equal(3, committed.Count);
            Assert.Equal(3, committed.Select(item => item.SessionSequence).Distinct().Count());
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Manual_confirm_targets_only_selected_monitoring_context()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextA = MonitoringContextId.CreateNew();
            var contextB = MonitoringContextId.CreateNew();
            var sourceA = GameplaySessionTestInfrastructure.DefaultSource("acct-a");
            var sourceB = GameplaySessionTestInfrastructure.DefaultSource("acct-b");
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                [
                    GameplaySessionTestInfrastructure.ReadyContext(contextA, sourceA),
                    GameplaySessionTestInfrastructure.ReadyContext(contextB, sourceB)
                ]));

            var recordA = repository.EstablishTrustedFromWelcome("acct-a", "Hero A");
            var recordB = repository.EstablishTrustedFromWelcome("acct-b", "Hero B");

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 structurally timestamped",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 structurally timestamped",
                    contextB,
                    sourceB,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 2);

            var confirm = manager.ConfirmCharacter(contextA, recordA.RecordId!);
            Assert.True(confirm.IsSuccess);

            var sessionA = manager.Current.Sessions.First(session => session.ContextId == contextA);
            var sessionB = manager.Current.Sessions.First(session => session.ContextId == contextB);

            Assert.Equal(CharacterIdentityConfidence.Confirmed, sessionA.CharacterIdentityConfidence);
            Assert.Equal(recordA.RecordId, sessionA.CharacterRecordId);
            Assert.NotEqual(CharacterIdentityConfidence.Confirmed, sessionB.CharacterIdentityConfidence);
            Assert.Null(sessionB.CharacterRecordId);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task RunUiDispatcherConfirmScenarioAsync()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            var identityReadService = new GameplaySessionIdentityReadService(
                manager,
                monitoring,
                repository);

            using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
                identityReadService,
                manager);

            var record = repository.EstablishTrustedFromWelcome("acct-1", "Known Hero");

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 structurally timestamped",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 1);

            var result = manager.ConfirmCharacter(contextId, record.RecordId!);
            Assert.True(result.IsSuccess);

            await PumpDispatcherAsync();

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
            Assert.Equal(record.RecordId, session.CharacterRecordId);
            Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);

            identityReadService.Dispose();
            await manager.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task PumpDispatcherAsync()
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(10);
        }
    }

    private sealed class FakeApplicationOrchestrator : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; set; } =
            ApplicationStateSnapshot.Empty(DateTimeOffset.UtcNow);

        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
