using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplayDrainTests
{
    [Fact]
    public async Task Barrier_waits_for_gameplay_and_does_not_finalize_or_freeze()
    {
        await using var f = await ParserDrainTests.Fixture.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var block = 0;
        using var manager = await CreateResolvedAsync(f, new GameplaySessionOptions
        {
            CombatSnapshotPublishInterval = TimeSpan.Zero,
            TestHooks = new() { BeforeProcessWorkItem = () =>
            {
                if (Interlocked.Exchange(ref block, 0) == 1) { entered.TrySetResult(); release.Wait(); }
            } }
        });
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        f.Directory.Append(f.Path, "You hit Test Enemy with your Fire Ball for 10.00 points of Fire damage.\n");
        block = 1;
        var drain = manager.DrainThroughAsync(f.Id, session);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(drain.IsCompleted);
        }
        finally { release.Set(); }
        var result = await drain;
        Assert.Equal(DrainOutcome.Success, result.Outcome);
        var current = Assert.Single(manager.Current.Sessions);
        Assert.Equal(session, current.SessionId);
        Assert.Null(current.FinalizedAt);
        Assert.Equal(GameplaySessionLifecycleState.Active, current.LifecycleState);
        Assert.Equal(1000, current.CombatAnalytics.Session.DamageDealt.Hundredths);
        await result.Fence!.ResumeAsync();
        f.Directory.Append(f.Path, "You hit Test Enemy with your Fire Ball for 2.00 points of Fire damage.\n");
        var next = await manager.DrainThroughAsync(f.Id, session);
        await using var held = next.Fence!;
        Assert.Equal(DrainOutcome.Success, next.Outcome);
        Assert.Equal(1200, Assert.Single(manager.Current.Sessions).CombatAnalytics.Session.DamageDealt.Hundredths);
    }

    [Fact]
    public async Task Wrong_expected_session_does_not_acquire_fence()
    {
        await using var f = await ParserDrainTests.Fixture.Create();
        using var manager = Create(f);
        await manager.StartAsync();
        Assert.Equal(DrainOutcome.SessionChanged, (await manager.DrainThroughAsync(f.Id, GameplaySessionId.CreateNew())).Outcome);
        var parser = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(DrainOutcome.Success, parser.Outcome);
        await parser.Fence!.AbortAsync();
    }

    [Fact]
    public async Task Replacement_while_parser_is_pending_rejects_old_session()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var f = await ParserDrainTests.Fixture.Create(new CallbackClassifier(raw =>
        { entered.TrySetResult(); release.Wait(); return new ParserClassifier().Classify(raw); }));
        using var manager = Create(f);
        await manager.StartAsync();
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        f.Directory.Append(f.Path, "chat\n");
        var drain = manager.DrainThroughAsync(f.Id, session);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var replaced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.StateChanged += (_, _) =>
            {
                if (manager.Current.Sessions.All(s => s.SessionId != session)) replaced.TrySetResult();
            };
            manager.ResetForNewRuntimeGeneration();
            await replaced.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.Set(); }
        Assert.Equal(DrainOutcome.SessionChanged, (await drain).Outcome);
        var next = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(DrainOutcome.Success, next.Outcome);
        await next.Fence!.AbortAsync();
    }

    [Fact]
    public async Task Processing_failure_cannot_be_erased_by_a_later_success()
    {
        await using var f = await ParserDrainTests.Fixture.Create();
        using var manager = await CreateResolvedAsync(f, combat: new FailingCombatParser());
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        f.Directory.Append(f.Path, "You hit Test Enemy with your Fire Ball for 1.00 points of Fire damage.\nchat\n");
        Assert.Equal(DrainOutcome.ProcessingFailed, (await manager.DrainThroughAsync(f.Id, session)).Outcome);
        Assert.Equal(DrainOutcome.ProcessingFailed, (await manager.DrainThroughAsync(f.Id, session)).Outcome);
        var parser = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(DrainOutcome.Success, parser.Outcome);
        await parser.Fence!.AbortAsync();
    }

    [Fact]
    public async Task Gameplay_stop_while_parser_pending_fails_and_releases_fence()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var f = await ParserDrainTests.Fixture.Create(new CallbackClassifier(raw =>
        { entered.TrySetResult(); release.Wait(); return new ParserClassifier().Classify(raw); }));
        using var manager = Create(f);
        await manager.StartAsync();
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        f.Directory.Append(f.Path, "chat\n");
        var drain = manager.DrainThroughAsync(f.Id, session);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await manager.StopAsync();
        }
        finally { release.Set(); }
        Assert.Equal(DrainOutcome.ServiceStopped, (await drain).Outcome);
        var parser = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(DrainOutcome.Success, parser.Outcome);
        await parser.Fence!.AbortAsync();
    }

    private static GameplaySessionManager Create(ParserDrainTests.Fixture f, GameplaySessionOptions? options = null,
        ICombatEventParser? combat = null) => new(f.Monitoring, f.Parser,
            new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = f.Directory.Root }), options,
            combatEventParser: combat);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_or_context_removal_cannot_authorize_gameplay_drain(bool remove)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var f = await ParserDrainTests.Fixture.Create(new CallbackClassifier(raw =>
        { entered.TrySetResult(); release.Wait(); return new ParserClassifier().Classify(raw); }));
        using var manager = Create(f);
        await manager.StartAsync();
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        using var cancel = new CancellationTokenSource();
        f.Directory.Append(f.Path, "chat\n");
        var drain = manager.DrainThroughAsync(f.Id, session, cancellationToken: cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (remove)
                f.Monitoring.Publish(ParserTestSnapshots.Snapshot(2, f.Context with
                { State = MonitoringContextState.Stopped, SourceBindingGeneration = 2, LastSourceBindingTransitionKind = MonitoringSourceTransitionKind.ContextRemoved }));
            else cancel.Cancel();
            var result = await drain.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(remove ? DrainOutcome.ContextGone : DrainOutcome.Cancelled, result.Outcome);
            Assert.Null(result.Fence);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task Drain_leaves_dedup_buffer_for_existing_finalization_flush()
    {
        await using var f = await ParserDrainTests.Fixture.Create();
        var repository = new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = f.Directory.Root });
        var record = repository.EstablishTrustedFromWelcome("acct-1", "Drain Hero", DateTimeOffset.UtcNow).RecordId!;
        var store = new SegmentStore(f.Directory.Root);
        using var manager = new GameplaySessionManager(f.Monitoring, f.Parser, repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero }, segmentStore: store)
        {
            CombatMirrorPolicy = MirrorCompatibilityPolicy.ForTests(new MirrorCompatibilityRule
            {
                RuleId = "drain-test", LeftFamily = CombatEventFamily.HealDealt, RightFamily = CombatEventFamily.HealReceived,
                RequiredDiscriminator = MirrorDiscriminatorKind.ActualSourceChannel, LeftChannel = "Delivery", RightChannel = "Receipt",
                MaxSequenceDistance = 2, FacetUnion = EventFacets.HealDelivered | EventFacets.HealReceived
            })
        };
        await manager.StartAsync();
        Assert.True(manager.ConfirmCharacter(f.Id, record).IsSuccess);
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        f.Directory.Append(f.Path, "You heal yourself for 10.00 hit points with Reconstruction.\n");
        var drain = await manager.DrainThroughAsync(f.Id, session);
        await using var hold = drain.Fence!;
        Assert.Equal(DrainOutcome.Success, drain.Outcome);
        Assert.Equal(0, Assert.Single(manager.Current.Sessions).CombatAnalytics.LogicalEventsApplied);
        Assert.Null(Assert.Single(manager.Current.Sessions).FinalizedAt);
        Assert.Empty(store.ListHeaders());
        await manager.StopAsync(); // Existing shutdown path, not the drain primitive, owns the flush.
        var persisted = store.TryLoad(session, 0).Segment!;
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Aggregates.LogicalEventsApplied);
        Assert.Equal(1000, persisted.Aggregates.Session.HealingDealt.Hundredths);
    }

    [Fact]
    public async Task Finish_session_drains_finalizes_persists_and_publishes_segment()
    {
        await using var f = await ParserDrainTests.Fixture.Create();
        var repository = new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = f.Directory.Root });
        var record = repository.EstablishTrustedFromWelcome("acct-1", "Drain Hero", DateTimeOffset.UtcNow).RecordId!;
        var store = new SegmentStore(f.Directory.Root);
        var published = new List<(GameplaySessionId SessionId, int Ordinal)>();
        store.SegmentPublished += (_, e) => published.Add((e.GameplaySessionId, e.SegmentOrdinal));
        using var manager = new GameplaySessionManager(f.Monitoring, f.Parser, repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero }, segmentStore: store);
        await manager.StartAsync();
        Assert.True(manager.ConfirmCharacter(f.Id, record).IsSuccess);
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        f.Directory.Append(f.Path, "You hit Test Enemy with your Fire Ball for 10.00 points of Fire damage.\n");

        var result = await manager.FinishSessionAsync(f.Id, session);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.False(result.ClosedParserFence);
        Assert.Equal([(session, 0)], published);
        var persisted = store.TryLoad(session, 0).Segment!;
        Assert.NotNull(persisted);
        Assert.Equal(1000, persisted.Aggregates.Session.DamageDealt.Hundredths);

        var again = await manager.FinishSessionAsync(f.Id, session);
        Assert.False(again.IsSuccess);
        Assert.False(again.ClosedParserFence);
        Assert.Equal(GameplaySessionOutcome.NoActiveSession, again.Outcome);
        Assert.Equal([(session, 0)], published);
    }

    [Fact]
    public async Task Gameplay_queue_rejection_fails_instead_of_acknowledging_prefix()
    {
        await using var f = await ParserDrainTests.Fixture.Create();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = 0;
        using var manager = Create(f, new GameplaySessionOptions
        {
            WorkQueueCapacity = 1, TestHooks = new() { BeforeProcessWorkItem = () =>
            { if (Interlocked.Exchange(ref armed, 0) == 1) { entered.TrySetResult(); release.Wait(); } } }
        });
        await manager.StartAsync();
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        armed = 1;
        manager.ResetForNewRuntimeGeneration(); // Occupy the consumer, then fill and overflow its queue.
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<GameplayDrainResult>? drain = null;
        try
        {
            manager.ResetForNewRuntimeGeneration();
            f.Directory.Append(f.Path, "chat\n");
            drain = manager.DrainThroughAsync(f.Id, session);
            Assert.Equal(DrainOutcome.Overloaded, (await drain.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task Processor_failure_completes_queued_drain_and_releases_its_fence()
    {
        await using var f = await ParserDrainTests.Fixture.Create();
        var acquired = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(DrainOutcome.Success, acquired.Outcome);
        await using var fence = acquired.Fence!;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = 0;
        using var manager = new GameplaySessionManager(f.Monitoring,
            new AcknowledgedFenceParser(f.Parser, fence),
            new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = f.Directory.Root }),
            new GameplaySessionOptions { TestHooks = new() { BeforeProcessWorkItem = () =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    entered.TrySetResult();
                    release.Wait();
                    throw new InvalidOperationException("Injected processor failure before queued drain.");
                }
            } } });
        await manager.StartAsync();
        var session = Assert.Single(manager.Current.Sessions).SessionId;
        armed = 1;
        manager.ResetForNewRuntimeGeneration();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var accepted = manager.GetDiagnostics().LastAcceptedWorkSequence;
            // Completed parser transport makes barrier admission synchronous and observable.
            var drain = manager.DrainThroughAsync(f.Id, session);
            Assert.Equal(accepted + 1, manager.GetDiagnostics().LastAcceptedWorkSequence);
            Assert.False(drain.IsCompleted);
            release.Set();
            Assert.Equal(DrainOutcome.ProcessingFailed,
                (await drain.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
            Assert.False(fence.IsHeld);
            var next = await f.Parser.PauseAndDrainThroughAsync(f.Id);
            Assert.Equal(DrainOutcome.Success, next.Outcome);
            await next.Fence!.AbortAsync();
        }
        finally { release.Set(); }
    }

    private sealed class AcknowledgedFenceParser(IParserManager inner, ParserFence fence) : IParserManager
    {
        public Task<ParserDrainResult> PauseAndDrainThroughAsync(MonitoringContextId contextId,
            ParserSourcePosition? boundary = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ParserDrainResult(DrainOutcome.Success, fence));
        public ParserManagerSnapshot Current => inner.Current;
        public ParserClassificationSnapshot ClassificationCurrent => inner.ClassificationCurrent;
        public event EventHandler<ParserManagerChangedEventArgs>? StateChanged
        { add => inner.StateChanged += value; remove => inner.StateChanged -= value; }
        public event EventHandler<ParserEventsAvailableEventArgs>? EventsAvailable
        { add => inner.EventsAvailable += value; remove => inner.EventsAvailable -= value; }
        public event EventHandler<ParserClassificationChangedEventArgs>? ClassificationChanged
        { add => inner.ClassificationChanged += value; remove => inner.ClassificationChanged -= value; }
        public event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailable
        { add => inner.ClassifiedEventsAvailable += value; remove => inner.ClassifiedEventsAvailable -= value; }
        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
        public ParserManagerDiagnostics GetDiagnostics() => inner.GetDiagnostics();
        public ParserClassificationDiagnostics GetClassificationDiagnostics() => inner.GetClassificationDiagnostics();
    }

    private static async Task<GameplaySessionManager> CreateResolvedAsync(ParserDrainTests.Fixture f,
        GameplaySessionOptions? options = null, ICombatEventParser? combat = null)
    {
        var repository = new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = f.Directory.Root });
        var record = repository.EstablishTrustedFromWelcome("acct-1", "Drain Hero", DateTimeOffset.UtcNow).RecordId!;
        var manager = new GameplaySessionManager(f.Monitoring, f.Parser, repository, options, combatEventParser: combat);
        await manager.StartAsync();
        Assert.True(manager.ConfirmCharacter(f.Id, record).IsSuccess);
        return manager;
    }

    private sealed class CallbackClassifier(Func<ParserRawEvent, ParserEvent> classify) : IParserClassifier
    { public ParserEvent Classify(ParserRawEvent rawEvent) => classify(rawEvent); }

    private sealed class FailingCombatParser : ICombatEventParser
    {
        public bool TryParse(ParserEvent item, out CombatEvent combatEvent) => throw new InvalidOperationException();
        public bool TryParseCanonical(ParserEvent item, out CanonicalCombatEvent combatEvent) => throw new InvalidOperationException();
        public bool TryAdaptToLegacy(CanonicalCombatEvent item, out CombatEvent combatEvent) => throw new InvalidOperationException();
    }
}
