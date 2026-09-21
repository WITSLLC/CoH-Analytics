using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class AuthoritativeProcessExitFinalizationTests
{
    private static readonly DateTimeOffset StartA = new(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = new(2026, 8, 14, 2, 15, 39, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Single_bound_process_exit_finalizes_and_publishes_once()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() => harness.Gameplay.Current.Sessions.Count == 0);
        Assert.Equal([(session, 0)], harness.Published);
        var persisted = harness.Store.TryLoad(session, 0).Segment!;
        Assert.Equal(1000, persisted.Aggregates.Session.DamageDealt.Hundredths);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, Assert.Single(harness.Monitoring.Current.Contexts).State);
    }

    [Fact]
    public async Task Two_clients_process_A_exit_finalizes_only_A()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        var clientA = harness.Runtime.RunningClients[0];
        var clientB = Client(6_356, StartB);
        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [clientA, clientB]);
        harness.PublishGrowing(revision: 2, harness.PrimarySource, harness.SecondarySource);
        var sessionB = await harness.WaitForActiveSessionAsync("AltAccount");
        harness.AppendDamage(harness.PrimaryPath);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [clientB]);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Count == 1
            && harness.Monitoring.Current.Contexts.Count == 1);
        var remaining = Assert.Single(harness.Gameplay.Current.Sessions);
        Assert.Equal(sessionB, remaining.SessionId);
        Assert.Equal(GameplaySessionLifecycleState.Active, remaining.LifecycleState);
        Assert.Equal([(sessionA, 0)], harness.Published);
        Assert.Equal("AltAccount", Assert.Single(harness.Monitoring.Current.Contexts).AccountStableId);
    }

    [Fact]
    public async Task Exiting_process_with_no_bound_context_does_not_finalize()
    {
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA), Client(6_356, StartB)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        Assert.Null(Assert.Single(harness.Monitoring.Current.Contexts).ProcessInstance);

        harness.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [Client(6_356, StartB)]);

        await harness.WaitForWorkDrainedAsync();
        Assert.Equal(session, Assert.Single(harness.Gameplay.Current.Sessions).SessionId);
        Assert.Empty(harness.Published);
        Assert.Equal(GameplaySessionLifecycleState.Active, Assert.Single(harness.Gameplay.Current.Sessions).LifecycleState);
    }

    [Fact]
    public async Task Two_contexts_bound_to_the_same_process_are_not_auto_finalized()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.PublishGrowing(revision: 2, harness.PrimarySource, harness.SecondarySource);
        var sessionB = await harness.WaitForActiveSessionAsync("AltAccount");
        Assert.Equal(2, harness.Monitoring.Current.Contexts.Count);
        Assert.All(harness.Monitoring.Current.Contexts, context => Assert.NotNull(context.ProcessInstance));

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Count == 2
            && harness.Gameplay.Current.Sessions.All(session =>
                session.LifecycleState == GameplaySessionLifecycleState.Suspended));
        Assert.Empty(harness.Published);
        Assert.Contains(harness.Gameplay.Current.Sessions, session => session.SessionId == sessionA);
        Assert.Contains(harness.Gameplay.Current.Sessions, session => session.SessionId == sessionB);
    }

    [Fact]
    public async Task Unbound_multi_client_state_does_not_finalize()
    {
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA), Client(6_356, StartB)]);
        await harness.WaitForActiveSessionAsync("TestAccount");
        Assert.Null(Assert.Single(harness.Monitoring.Current.Contexts).ProcessInstance);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Count == 1
            && harness.Gameplay.Current.Sessions[0].LifecycleState
                == GameplaySessionLifecycleState.Suspended);
        Assert.Empty(harness.Published);
    }

    [Fact]
    public async Task Stale_process_identity_does_not_finalize()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;

        var result = await harness.Gameplay.FinishSessionForAuthoritativeProcessExitAsync(
            contextId,
            Client(9_999, StartB),
            session);

        Assert.False(result.IsSuccess);
        Assert.Equal(session, Assert.Single(harness.Gameplay.Current.Sessions).SessionId);
        Assert.Empty(harness.Published);
    }

    [Fact]
    public async Task Generation_reset_during_held_exit_fence_does_not_finalize_successor()
    {
        SignalingDrainParser? gate = null;
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            parserFactory: inner => gate = new SignalingDrainParser(inner));
        var original = await harness.WaitForActiveSessionAsync("TestAccount");

        var exit = Task.Run(() =>
            harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []));
        await gate!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Gameplay.ResetForNewRuntimeGeneration();
        gate.Release.TrySetResult();
        await exit.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(harness.Published, item => item.SessionId != original);
        Assert.All(
            harness.Gameplay.Current.Sessions,
            session => Assert.NotEqual(original, session.SessionId));
    }

    [Fact]
    public async Task Manual_finish_first_then_process_exit_publish_at_most_once()
    {
        SignalingDrainParser? gate = null;
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            parserFactory: inner => gate = new SignalingDrainParser(inner));
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;

        var manual = harness.Gameplay.FinishSessionAsync(contextId, session);
        await gate!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var exit = Task.Run(() =>
            harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []));
        gate.Release.TrySetResult();
        var manualResult = await manual.WaitAsync(TimeSpan.FromSeconds(10));
        await exit.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(manualResult.IsSuccess || harness.Published.Count == 1);
        Assert.Equal([(session, 0)], harness.Published);
        Assert.DoesNotContain(harness.Gameplay.Current.Sessions, candidate => candidate.SessionId == session);
    }

    [Fact]
    public async Task Process_exit_first_then_manual_finish_publish_at_most_once()
    {
        SignalingDrainParser? gate = null;
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            parserFactory: inner => gate = new SignalingDrainParser(inner));
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;

        var exit = Task.Run(() =>
            harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []));
        await gate!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var manual = harness.Gameplay.FinishSessionAsync(contextId, session);
        gate.Release.TrySetResult();
        var manualResult = await manual.WaitAsync(TimeSpan.FromSeconds(10));
        await exit.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(manualResult.IsSuccess || harness.Published.Count == 1);
        Assert.Equal([(session, 0)], harness.Published);
        Assert.DoesNotContain(harness.Gameplay.Current.Sessions, candidate => candidate.SessionId == session);
    }

    [Fact]
    public async Task Drain_failure_during_process_exit_does_not_finalize()
    {
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            parserFactory: inner => new FaultingDrainParser(inner));
        var session = await harness.WaitForActiveSessionAsync("TestAccount");

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Count == 1
            && harness.Gameplay.Current.Sessions[0].LifecycleState
                == GameplaySessionLifecycleState.Suspended);
        Assert.Equal(session, Assert.Single(harness.Gameplay.Current.Sessions).SessionId);
        Assert.Empty(harness.Published);
    }

    [Fact]
    public async Task Gameplay_processing_failure_during_exit_does_not_fabricate_success()
    {
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            combat: new FailingCombatParser());
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForWorkDrainedAsync();
        Assert.Empty(harness.Published);
        Assert.DoesNotContain(
            harness.Store.ListHeaders(),
            header => header.GameplaySessionId == session);
    }

    [Fact]
    public async Task Context_replaced_before_finish_does_not_finalize_successor()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var armed = 0;
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            options: new GameplaySessionOptions
            {
                CombatSnapshotPublishInterval = TimeSpan.Zero,
                TestHooks = new GameplaySessionTestHooks
                {
                    BeforeProcessWorkItem = () =>
                    {
                        if (Interlocked.Exchange(ref armed, 0) == 1)
                        {
                            entered.TrySetResult();
                            release.Wait();
                        }
                    }
                }
            });
        var original = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        armed = 1;
        var finish = harness.Gameplay.FinishSessionForAuthoritativeProcessExitAsync(
            contextId,
            harness.Runtime.RunningClients[0],
            original);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Gameplay.ResetForNewRuntimeGeneration();
        release.Set();
        var result = await finish.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.All(
            harness.Gameplay.Current.Sessions,
            session => Assert.NotEqual(original, session.SessionId));
    }

    [Fact]
    public async Task Other_client_stays_active_while_exiting_client_drain_is_held()
    {
        SignalingDrainParser? gate = null;
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            parserFactory: inner => gate = new SignalingDrainParser(inner));
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextA = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        var clientA = harness.Runtime.RunningClients[0];
        var clientB = Client(6_356, StartB);
        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [clientA, clientB]);
        harness.PublishGrowing(revision: 2, harness.PrimarySource, harness.SecondarySource);
        var sessionB = await harness.WaitForActiveSessionAsync("AltAccount");
        gate!.ContextFilter = contextA;

        var exit = Task.Run(() =>
            harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [clientB]));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var remaining = Assert.Single(
            harness.Gameplay.Current.Sessions,
            session => session.SessionId == sessionB);
        Assert.Equal(GameplaySessionLifecycleState.Active, remaining.LifecycleState);
        var otherDrain = await harness.Parser.PauseAndDrainThroughAsync(
            remaining.ContextId).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DrainOutcome.Success, otherDrain.Outcome);
        if (otherDrain.Fence is not null)
        {
            await otherDrain.Fence.AbortAsync();
        }

        gate.Release.TrySetResult();
        await exit.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([(sessionA, 0)], harness.Published);
        Assert.Equal(sessionB, Assert.Single(harness.Gameplay.Current.Sessions).SessionId);
        Assert.Equal(GameplaySessionLifecycleState.Active, Assert.Single(harness.Gameplay.Current.Sessions).LifecycleState);
    }

    [Fact]
    public async Task Proven_exit_with_other_clients_remaining_does_not_leave_a_successor_session()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        var clientA = harness.Runtime.RunningClients[0];
        var clientB = Client(6_356, StartB);
        var clientC = Client(7_421, StartB.AddHours(1));
        harness.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientA, clientB, clientC]);

        harness.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientB, clientC]);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.All(session => session.SessionId != sessionA)
            && harness.Monitoring.Current.Contexts.All(context =>
                context.ProcessInstance is not { ProcessId: 3_524 }));
        await harness.WaitForWorkDrainedAsync();
        Assert.Equal([(sessionA, 0)], harness.Published);
        Assert.DoesNotContain(harness.Gameplay.Current.Sessions, session => session.SessionId == sessionA);
        Assert.DoesNotContain(
            harness.Monitoring.Current.Contexts,
            context => context.State == MonitoringContextState.Ready
                && context.ProcessInstance is { } process
                && process.ProcessId == 3_524);
    }

    [Fact]
    public async Task Duplicate_exit_notification_does_not_publish_twice()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        await harness.WaitForAsync(() => harness.Gameplay.Current.Sessions.All(item => item.SessionId != session));
        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Off, []);
        await harness.WaitForWorkDrainedAsync();

        Assert.Equal([(session, 0)], harness.Published);
    }

    [Fact]
    public async Task Process_identity_refinement_is_not_treated_as_exit()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        var refined = Client(3_524, StartA.AddMinutes(-2));

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [refined]);
        await harness.WaitForWorkDrainedAsync();

        Assert.Equal(session, Assert.Single(harness.Gameplay.Current.Sessions).SessionId);
        Assert.Equal(GameplaySessionLifecycleState.Active, Assert.Single(harness.Gameplay.Current.Sessions).LifecycleState);
        Assert.Empty(harness.Published);
        Assert.Equal(refined, Assert.Single(harness.Monitoring.Current.Contexts).ProcessInstance);
    }

    [Fact]
    public async Task Pid_reuse_with_later_start_finalizes_original_session_only()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var original = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);
        var reused = Client(3_524, StartB);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [reused]);
        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.All(session => session.SessionId != original));

        Assert.Equal([(original, 0)], harness.Published);
        Assert.DoesNotContain(harness.Gameplay.Current.Sessions, session => session.SessionId == original);
        Assert.DoesNotContain(
            harness.Monitoring.Current.Contexts,
            context => context.ProcessInstance is { } process
                && process.ProcessId == 3_524
                && process.ProcessStartTime == StartA);
    }

    [Fact]
    public async Task Two_to_zero_finalizes_only_the_bound_context()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        var clientA = harness.Runtime.RunningClients[0];
        var clientB = Client(6_356, StartB);
        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [clientA, clientB]);
        harness.PublishGrowing(revision: 2, harness.PrimarySource, harness.SecondarySource);
        var sessionB = await harness.WaitForActiveSessionAsync("AltAccount");

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.All(session => session.SessionId != sessionA)
            && harness.Gameplay.Current.Sessions.Any(session =>
                session.SessionId == sessionB
                && session.LifecycleState == GameplaySessionLifecycleState.Suspended));
        Assert.Equal([(sessionA, 0)], harness.Published);
        Assert.Equal(sessionB, Assert.Single(harness.Gameplay.Current.Sessions).SessionId);
        Assert.DoesNotContain(harness.Gameplay.Current.Sessions, session => session.SessionId == sessionA);
    }

    [Fact]
    public async Task Persist_failure_on_last_client_suspends_without_a_successor()
    {
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            segmentStore: new FailingPersistStore());
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        harness.AppendDamage(harness.PrimaryPath);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Count == 0
            && harness.Monitoring.Current.Contexts.Count == 1
            && harness.Monitoring.Current.Contexts[0].State
                == MonitoringContextState.RuntimeSuspended);
        Assert.Empty(harness.Published);
        Assert.Empty(harness.Store.ListHeaders());
        Assert.DoesNotContain(
            harness.Gameplay.Current.Sessions,
            candidate => candidate.SessionId == session);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, Assert.Single(harness.Monitoring.Current.Contexts).State);
        Assert.Equal(contextId, Assert.Single(harness.Monitoring.Current.Contexts).ContextId);
    }

    [Fact]
    public async Task Persist_failure_with_remaining_clients_retires_the_exited_context()
    {
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            segmentStore: new FailingPersistStore());
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextA = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        var clientA = harness.Runtime.RunningClients[0];
        var clientB = Client(6_356, StartB);
        var clientC = Client(7_421, StartB.AddHours(1));
        harness.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientA, clientB, clientC]);

        harness.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientB, clientC]);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.All(session => session.SessionId != sessionA)
            && harness.Monitoring.Current.Contexts.All(context => context.ContextId != contextA)
            && harness.Parser.Current.Workers.All(worker => worker.ContextId != contextA));
        await harness.WaitForWorkDrainedAsync();
        Assert.Empty(harness.Published);
        Assert.Empty(harness.Store.ListHeaders());
        Assert.DoesNotContain(
            harness.Monitoring.Current.Contexts,
            context => context.ContextId == contextA
                || (context.State == MonitoringContextState.Ready
                    && context.ProcessInstance is { ProcessId: 3_524 }));
        Assert.DoesNotContain(
            harness.Parser.Current.Workers,
            worker => worker.ContextId == contextA);
    }

    [Fact]
    public async Task Drain_failure_with_remaining_clients_keeps_the_session_ready()
    {
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            parserFactory: inner => new FaultingDrainParser(inner));
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextA = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        var clientA = harness.Runtime.RunningClients[0];
        var clientB = Client(6_356, StartB);
        var clientC = Client(7_421, StartB.AddHours(1));
        harness.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientA, clientB, clientC]);

        harness.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientB, clientC]);

        await harness.WaitForWorkDrainedAsync();
        var remaining = Assert.Single(harness.Gameplay.Current.Sessions);
        Assert.Equal(sessionA, remaining.SessionId);
        Assert.Equal(GameplaySessionLifecycleState.Active, remaining.LifecycleState);
        var context = Assert.Single(harness.Monitoring.Current.Contexts);
        Assert.Equal(contextA, context.ContextId);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Empty(harness.Published);
        Assert.DoesNotContain(
            harness.Parser.Current.Workers,
            worker => worker.ContextId == contextA && worker.State == ParserWorkerState.Stopped);
    }

    [Fact]
    public async Task StateChanged_subscriber_throw_does_not_block_process_exit_finalization()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);
        harness.Monitoring.StateChanged += (_, _) =>
            throw new InvalidOperationException("monitoring subscriber failed");

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Count == 0
            && harness.Monitoring.Current.Contexts.Count == 1
            && harness.Monitoring.Current.Contexts[0].State
                == MonitoringContextState.RuntimeSuspended);
        Assert.Equal([(session, 0)], harness.Published);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, Assert.Single(harness.Monitoring.Current.Contexts).State);
    }

    [Fact]
    public async Task Service_stop_during_held_exit_drain_completes_without_a_stranded_fence()
    {
        SignalingDrainParser? gate = null;
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            parserFactory: inner => gate = new SignalingDrainParser(inner));
        await harness.WaitForActiveSessionAsync("TestAccount");

        var exit = Task.Run(() =>
            harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []));
        await gate!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Monitoring.StopAsync();
        await harness.Gameplay.StopAsync();
        gate.Release.TrySetResult();
        await exit.WaitAsync(TimeSpan.FromSeconds(10));

        var leftover = await harness.Parser.PauseAndDrainThroughAsync(
            Assert.Single(harness.Monitoring.GetDiagnostics().Contexts).ContextId)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(DrainOutcome.AlreadyHeld, leftover.Outcome);
        if (leftover.Fence is not null)
        {
            await leftover.Fence.AbortAsync();
        }
    }

    [Fact]
    public async Task Manual_finish_resumes_parser_and_allows_a_successor_session()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        harness.AppendDamage(harness.PrimaryPath);

        var result = await harness.Gameplay.FinishSessionAsync(contextId, session);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.False(result.ClosedParserFence);
        Assert.Equal([(session, 0)], harness.Published);
        Assert.Equal(MonitoringContextState.Ready, Assert.Single(harness.Monitoring.Current.Contexts).State);
        Assert.DoesNotContain(
            harness.Parser.Current.Workers,
            worker => worker.ContextId == contextId && worker.State == ParserWorkerState.Stopped);

        harness.AppendDamage(harness.PrimaryPath);
        var drain = await harness.Parser.PauseAndDrainThroughAsync(contextId)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DrainOutcome.Success, drain.Outcome);
        if (drain.Fence is not null)
        {
            await drain.Fence.AbortAsync();
        }

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Any(candidate =>
                candidate.ContextId == contextId
                && candidate.SessionId != session
                && candidate.LifecycleState == GameplaySessionLifecycleState.Active));
        var successor = Assert.Single(
            harness.Gameplay.Current.Sessions,
            candidate => candidate.SessionId != session);
        Assert.Equal(GameplaySessionLifecycleState.Active, successor.LifecycleState);
        Assert.Equal([(session, 0)], harness.Published);
    }

    [Fact]
    public async Task SegmentPublished_throw_after_commit_does_not_duplicate_or_leave_a_zombie()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        var process = harness.Runtime.RunningClients[0];
        harness.AppendDamage(harness.PrimaryPath);
        harness.Store.SegmentPublished += (_, _) =>
            throw new InvalidOperationException("analytics subscriber failed");

        var result = await harness.Gameplay.FinishSessionForAuthoritativeProcessExitAsync(
            contextId,
            process,
            session);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.True(result.ClosedParserFence);
        Assert.Equal([(session, 0)], harness.Published);
        Assert.Equal(session, Assert.Single(harness.Store.ListHeaders()).GameplaySessionId);
        Assert.NotNull(harness.Store.TryLoad(session, 0).Segment);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.Count == 0
            && harness.Monitoring.Current.Contexts.Count == 1
            && harness.Monitoring.Current.Contexts[0].State
                == MonitoringContextState.RuntimeSuspended);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, Assert.Single(harness.Monitoring.Current.Contexts).State);

        var retry = await harness.Gameplay.FinishSessionForAuthoritativeProcessExitAsync(
            contextId,
            process,
            session);
        Assert.False(retry.IsSuccess);
        Assert.False(retry.ClosedParserFence);
        Assert.Equal(GameplaySessionOutcome.NoActiveSession, retry.Outcome);
        Assert.Equal([(session, 0)], harness.Published);
        Assert.Equal(session, Assert.Single(harness.Store.ListHeaders()).GameplaySessionId);
    }

    [Fact]
    public async Task Cancellation_after_finish_work_is_admitted_does_not_retry_a_committed_segment()
    {
        var cancelFinish = 0;
        using var cts = new CancellationTokenSource();
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(3_524, StartA)],
            options: new GameplaySessionOptions
            {
                CombatSnapshotPublishInterval = TimeSpan.Zero,
                TestHooks = new GameplaySessionTestHooks
                {
                    AfterCommandAdmission = () =>
                    {
                        if (Volatile.Read(ref cancelFinish) == 1)
                        {
                            cts.Cancel();
                        }
                    }
                }
            });
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextId = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        var process = harness.Runtime.RunningClients[0];
        harness.AppendDamage(harness.PrimaryPath);
        Volatile.Write(ref cancelFinish, 1);

        var result = await harness.Gameplay.FinishSessionForAuthoritativeProcessExitAsync(
            contextId,
            process,
            session,
            cts.Token);

        Assert.True(result.ClosedParserFence);
        if (result.IsSuccess)
        {
            Assert.Equal([(session, 0)], harness.Published);
            Assert.NotNull(harness.Store.TryLoad(session, 0).Segment);
        }
        else
        {
            Assert.Equal(GameplaySessionOutcome.ProcessingFailed, result.Outcome);
            Assert.True(
                harness.Published.Count is 0 or 1,
                $"Unexpected publication count {harness.Published.Count}.");
        }

        var retry = await harness.Gameplay.FinishSessionForAuthoritativeProcessExitAsync(
            contextId,
            process,
            session);
        Assert.False(retry.IsSuccess);
        Assert.Equal(GameplaySessionOutcome.NoActiveSession, retry.Outcome);
        Assert.True(harness.Published.Count <= 1);
        Assert.True(harness.Store.ListHeaders().Count <= 1);
    }

    [Theory]
    [InlineData(3_524, 6_356)]
    [InlineData(6_356, 3_524)]
    public async Task Two_proven_exits_in_one_event_keep_sibling_cleanup_when_one_drain_fails(
        int processIdA,
        int processIdB)
    {
        FaultingDrainParser? faulting = null;
        await using var harness = await ExitHarness.CreateAsync(
            clients: [Client(processIdA, StartA)],
            parserFactory: inner => faulting = new FaultingDrainParser(inner));
        var originalA = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextA = Assert.Single(harness.Monitoring.Current.Contexts).ContextId;
        var clientA = harness.Runtime.RunningClients[0];
        var clientB = Client(processIdB, StartB);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        await harness.WaitForAsync(() =>
            harness.Monitoring.Current.Contexts.Count == 1
            && harness.Monitoring.Current.Contexts[0].State
                == MonitoringContextState.RuntimeSuspended
            && harness.Gameplay.Current.Sessions.Any(session =>
                session.SessionId == originalA
                && session.LifecycleState == GameplaySessionLifecycleState.Suspended));
        Assert.Empty(harness.Published);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [clientB]);
        harness.PublishGrowing(revision: 2, harness.PrimarySource, harness.SecondarySource);
        var sessionB = await harness.WaitForActiveSessionAsync("AltAccount");
        var sessionA = await harness.WaitForActiveSessionAsync("TestAccount");
        var contextB = Assert.Single(
            harness.Monitoring.Current.Contexts,
            context => context.AccountStableId == "AltAccount").ContextId;
        Assert.Equal(2, harness.Monitoring.Current.Contexts.Count);
        Assert.Contains(
            harness.Monitoring.Current.Contexts,
            context => context.ContextId == contextA && context.ProcessInstance == clientA);
        Assert.Contains(
            harness.Monitoring.Current.Contexts,
            context => context.ContextId == contextB && context.ProcessInstance == clientB);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [clientA, clientB]);
        faulting!.ContextFilter = contextB;
        harness.AppendDamage(harness.PrimaryPath);

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.All(session => session.SessionId != sessionA)
            && harness.Monitoring.Current.Contexts.Count == 2
            && harness.Monitoring.Current.Contexts.All(context =>
                context.State == MonitoringContextState.RuntimeSuspended));
        Assert.Contains(harness.Published, item => item.SessionId == sessionA);
        Assert.DoesNotContain(harness.Published, item => item.SessionId == sessionB);
        Assert.NotNull(harness.Store.TryLoad(sessionA, 0).Segment);
        Assert.Contains(
            harness.Gameplay.Current.Sessions,
            session => session.SessionId == sessionB
                && session.LifecycleState == GameplaySessionLifecycleState.Suspended);
        Assert.DoesNotContain(harness.Gameplay.Current.Sessions, session => session.SessionId == sessionA);
        Assert.All(
            harness.Monitoring.Current.Contexts,
            context => Assert.Equal(MonitoringContextState.RuntimeSuspended, context.State));
    }

    [Fact]
    public async Task Intermediate_StateChanged_manual_finish_does_not_publish_twice()
    {
        await using var harness = await ExitHarness.CreateAsync(clients: [Client(3_524, StartA)]);
        var session = await harness.WaitForActiveSessionAsync("TestAccount");
        harness.AppendDamage(harness.PrimaryPath);
        var finishing = 0;
        harness.Monitoring.StateChanged += (_, e) =>
        {
            if (Interlocked.Exchange(ref finishing, 1) != 0)
            {
                return;
            }

            var context = e.Snapshot.Contexts.FirstOrDefault();
            var live = harness.Gameplay.Current.Sessions.FirstOrDefault(candidate =>
                candidate.SessionId == session);
            if (context is null || live is null)
            {
                return;
            }

            harness.Gameplay.FinishSessionAsync(context.ContextId, live.SessionId)
                .GetAwaiter()
                .GetResult();
        };

        harness.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        await harness.WaitForAsync(() =>
            harness.Gameplay.Current.Sessions.All(candidate => candidate.SessionId != session)
            && harness.Monitoring.Current.Contexts.Count == 1
            && harness.Monitoring.Current.Contexts[0].State
                == MonitoringContextState.RuntimeSuspended);
        Assert.Equal([(session, 0)], harness.Published);
        Assert.NotNull(harness.Store.TryLoad(session, 0).Segment);
    }

    private static HomecomingProcessInstance Client(int processId, DateTimeOffset start) =>
        FakeGameRuntimeService.CreateClient(processId, start);

    private sealed class ExitHarness : IAsyncDisposable
    {
        private readonly string _repositoryDirectory;
        private readonly ParserTestDirectory _logs = new();

        private ExitHarness(
            IReadOnlyList<HomecomingProcessInstance> clients,
            GameplaySessionOptions? options,
            ICombatEventParser? combat,
            Func<IParserManager, IParserManager>? parserFactory,
            ISegmentStore? segmentStore)
        {
            var welcomeA = Encoding.UTF8.GetBytes("[03:57] Welcome to City of Heroes, Dawn's Vanguard!\r\n");
            var welcomeB = Encoding.UTF8.GetBytes("[03:57] Welcome to City of Heroes, D4wn's Vanguard!\r\n");
            PrimaryPath = _logs.CreateFile("chatlog-a.txt", welcomeA);
            SecondaryPath = _logs.CreateFile("chatlog-b.txt", welcomeB);
            PrimarySource = ParserTestSnapshots.Source(PrimaryPath, "TestAccount");
            SecondarySource = ParserTestSnapshots.Source(SecondaryPath, "AltAccount");
            Time = new ManualTimeProvider(ObservedAt);
            Runtime = new FakeGameRuntimeService
            {
                CurrentStatus = GameRuntimeStatus.Running,
                RunningClients = clients,
                RunningClientCount = clients.Count
            };
            LogActivity = new FakeLogActivityService();
            Monitoring = new MonitoringSessionManager(
                Runtime,
                LogActivity,
                new MonitoringSessionManagerOptions { TimeProvider = Time });
            IParserManager parser = new ParserManager(Monitoring, new ParserManagerOptions
            {
                PollInterval = TimeSpan.FromDays(1),
                ReadBufferSize = 4,
                EventQueueCapacity = 128
            });
            Parser = parserFactory is null ? parser : parserFactory(parser);
            var repository = GameplaySessionTestInfrastructure.CreateRepository(out _repositoryDirectory);
            RecordA = repository.EstablishTrustedFromWelcome(
                "TestAccount",
                "Dawn's Vanguard",
                ObservedAt).RecordId!;
            RecordB = repository.EstablishTrustedFromWelcome(
                "AltAccount",
                "D4wn's Vanguard",
                ObservedAt).RecordId!;
            Store = segmentStore ?? new SegmentStore(_repositoryDirectory);
            Store.SegmentPublished += (_, e) => Published.Add((e.GameplaySessionId, e.SegmentOrdinal));
            Gameplay = new GameplaySessionManager(
                Monitoring,
                Parser,
                repository,
                options ?? new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
                combatEventParser: combat,
                segmentStore: Store);
            Monitoring.AuthoritativeSessionFinalizer = Gameplay;
        }

        public string PrimaryPath { get; }

        public string SecondaryPath { get; }

        public LogSourceId PrimarySource { get; }

        public LogSourceId SecondarySource { get; }

        public FakeGameRuntimeService Runtime { get; }

        public FakeLogActivityService LogActivity { get; }

        public ManualTimeProvider Time { get; }

        public MonitoringSessionManager Monitoring { get; }

        public IParserManager Parser { get; }

        public GameplaySessionManager Gameplay { get; }

        public ISegmentStore Store { get; }

        public CharacterRecordId RecordA { get; }

        public CharacterRecordId RecordB { get; }

        public List<(GameplaySessionId SessionId, int Ordinal)> Published { get; } = [];

        public static async Task<ExitHarness> CreateAsync(
            IReadOnlyList<HomecomingProcessInstance> clients,
            GameplaySessionOptions? options = null,
            ICombatEventParser? combat = null,
            Func<IParserManager, IParserManager>? parserFactory = null,
            ISegmentStore? segmentStore = null)
        {
            var harness = new ExitHarness(clients, options, combat, parserFactory, segmentStore);
            harness.PublishGrowing(revision: 1, harness.PrimarySource);
            await harness.Monitoring.StartAsync();
            await StartParserAsync(harness.Parser);
            await harness.Gameplay.StartAsync();
            return harness;
        }

        public void PublishGrowing(long revision, params LogSourceId[] sources)
        {
            LogActivity.Current = TestLogCandidates.Snapshot(
                revision,
                Time.GetUtcNow(),
                [.. sources.Select(source =>
                {
                    var length = new FileInfo(source.FilePath).Length;
                    return TestLogCandidates.Create(
                        source,
                        LogSourceActivityState.Growing,
                        Time.GetUtcNow(),
                        length: length,
                        previousLength: Math.Max(0, length - 1));
                })]);
            if (revision > 1)
            {
                LogActivity.RaiseActivityChanged();
            }
        }

        public void AppendDamage(string path) =>
            _logs.Append(path, "You hit Test Enemy with your Fire Ball for 10.00 points of Fire damage.\n");

        public async Task<GameplaySessionId> WaitForActiveSessionAsync(string account)
        {
            await WaitForAsync(() =>
                Gameplay.Current.Sessions.Any(session =>
                    session.AccountStableId == account
                    && session.LifecycleState == GameplaySessionLifecycleState.Active));
            var session = Gameplay.Current.Sessions.Single(candidate => candidate.AccountStableId == account);
            var record = account == "TestAccount" ? RecordA : RecordB;
            Assert.True(Gameplay.ConfirmCharacter(session.ContextId, record).IsSuccess);
            await WaitForWorkDrainedAsync();
            return session.SessionId;
        }

        public Task WaitForWorkDrainedAsync() =>
            GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(Gameplay);

        public async Task WaitForAsync(Func<bool> condition)
        {
            if (condition())
            {
                return;
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnGameplay(object? sender, GameplaySessionManagerChangedEventArgs e)
            {
                if (condition())
                {
                    done.TrySetResult();
                }
            }

            void OnMonitoring(object? sender, MonitoringSessionManagerChangedEventArgs e)
            {
                if (condition())
                {
                    done.TrySetResult();
                }
            }

            void OnParser(object? sender, ParserManagerChangedEventArgs e)
            {
                if (condition())
                {
                    done.TrySetResult();
                }
            }

            Gameplay.StateChanged += OnGameplay;
            Monitoring.StateChanged += OnMonitoring;
            Parser.StateChanged += OnParser;
            try
            {
                if (condition())
                {
                    return;
                }

                await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                Gameplay.StateChanged -= OnGameplay;
                Monitoring.StateChanged -= OnMonitoring;
                Parser.StateChanged -= OnParser;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Gameplay.StopAsync();
            await StopParserAsync(Parser);
            await Monitoring.StopAsync();
            Gameplay.Dispose();
            if (Parser is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (Parser is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Monitoring.Dispose();
            _logs.Dispose();
            try
            {
                Directory.Delete(_repositoryDirectory, recursive: true);
            }
            catch
            {
            }
        }

        private static Task StartParserAsync(IParserManager parser) => parser switch
        {
            FaultingDrainParser faulting => faulting.StartInnerAsync(),
            SignalingDrainParser signaling => signaling.StartInnerAsync(),
            _ => parser.StartAsync()
        };

        private static Task StopParserAsync(IParserManager parser) => parser switch
        {
            FaultingDrainParser faulting => faulting.StopInnerAsync(),
            SignalingDrainParser signaling => signaling.StopInnerAsync(),
            _ => parser.StopAsync()
        };
    }

    private sealed class SignalingDrainParser(IParserManager inner) : IParserManager, IAsyncDisposable
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MonitoringContextId? ContextFilter { get; set; }

        public async Task<ParserDrainResult> PauseAndDrainThroughAsync(
            MonitoringContextId contextId,
            ParserSourcePosition? boundary = null,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.PauseAndDrainThroughAsync(contextId, boundary, cancellationToken)
                .ConfigureAwait(false);
            if (result.Outcome != DrainOutcome.Success
                || (ContextFilter is { } filter && filter != contextId))
            {
                return result;
            }

            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (OperationCanceledException)
            {
                if (result.Fence is not null)
                {
                    await result.Fence.AbortAsync().ConfigureAwait(false);
                }

                throw;
            }
        }

        public ParserManagerSnapshot Current => inner.Current;

        public ParserClassificationSnapshot ClassificationCurrent => inner.ClassificationCurrent;

        public event EventHandler<ParserManagerChangedEventArgs>? StateChanged
        {
            add => inner.StateChanged += value;
            remove => inner.StateChanged -= value;
        }

        public event EventHandler<ParserEventsAvailableEventArgs>? EventsAvailable
        {
            add => inner.EventsAvailable += value;
            remove => inner.EventsAvailable -= value;
        }

        public event EventHandler<ParserClassificationChangedEventArgs>? ClassificationChanged
        {
            add => inner.ClassificationChanged += value;
            remove => inner.ClassificationChanged -= value;
        }

        public event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailable
        {
            add => inner.ClassifiedEventsAvailable += value;
            remove => inner.ClassifiedEventsAvailable -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            inner.StopAsync(cancellationToken);

        public ParserManagerDiagnostics GetDiagnostics() => inner.GetDiagnostics();

        public ParserClassificationDiagnostics GetClassificationDiagnostics() =>
            inner.GetClassificationDiagnostics();

        public Task StartInnerAsync() => inner.StartAsync();

        public Task StopInnerAsync() => inner.StopAsync();

        public async ValueTask DisposeAsync()
        {
            Release.TrySetResult();
            if (inner is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private sealed class FaultingDrainParser(IParserManager inner) : IParserManager, IAsyncDisposable
    {
        public MonitoringContextId? ContextFilter { get; set; }

        public Task<ParserDrainResult> PauseAndDrainThroughAsync(
            MonitoringContextId contextId,
            ParserSourcePosition? boundary = null,
            CancellationToken cancellationToken = default) =>
            ContextFilter is { } filter && filter != contextId
                ? inner.PauseAndDrainThroughAsync(contextId, boundary, cancellationToken)
                : Task.FromResult(new ParserDrainResult(DrainOutcome.ParserFault));

        public ParserManagerSnapshot Current => inner.Current;

        public ParserClassificationSnapshot ClassificationCurrent => inner.ClassificationCurrent;

        public event EventHandler<ParserManagerChangedEventArgs>? StateChanged
        {
            add => inner.StateChanged += value;
            remove => inner.StateChanged -= value;
        }

        public event EventHandler<ParserEventsAvailableEventArgs>? EventsAvailable
        {
            add => inner.EventsAvailable += value;
            remove => inner.EventsAvailable -= value;
        }

        public event EventHandler<ParserClassificationChangedEventArgs>? ClassificationChanged
        {
            add => inner.ClassificationChanged += value;
            remove => inner.ClassificationChanged -= value;
        }

        public event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailable
        {
            add => inner.ClassifiedEventsAvailable += value;
            remove => inner.ClassifiedEventsAvailable -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);

        public ParserManagerDiagnostics GetDiagnostics() => inner.GetDiagnostics();

        public ParserClassificationDiagnostics GetClassificationDiagnostics() =>
            inner.GetClassificationDiagnostics();

        public Task StartInnerAsync() => inner.StartAsync();

        public Task StopInnerAsync() => inner.StopAsync();

        public async ValueTask DisposeAsync()
        {
            if (inner is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private sealed class FailingCombatParser : ICombatEventParser
    {
        public bool TryParse(ParserEvent item, out CombatEvent combatEvent)
        {
            FailIfCombatLine(item);
            combatEvent = default!;
            return false;
        }

        public bool TryParseCanonical(ParserEvent item, out CanonicalCombatEvent combatEvent)
        {
            FailIfCombatLine(item);
            combatEvent = default!;
            return false;
        }

        public bool TryAdaptToLegacy(CanonicalCombatEvent item, out CombatEvent combatEvent) =>
            throw new InvalidOperationException();

        private static void FailIfCombatLine(ParserEvent item)
        {
            if (item.RawLine.Contains("points of", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException();
            }
        }
    }

    private sealed class FailingPersistStore : ISegmentStore
    {
        public string SegmentsDirectory => string.Empty;

        public string ManifestsDirectory => string.Empty;

        public event EventHandler<SegmentPublishedEventArgs>? SegmentPublished
        {
            add { }
            remove { }
        }

        public SegmentPersistResult Persist(SegmentDraft draft) =>
            new()
            {
                Outcome = SegmentPersistOutcome.PersistenceFailed,
                Detail = "injected persist failure"
            };

        public SegmentDeleteResult Delete(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
            new() { Outcome = SegmentDeleteOutcome.NotFound };

        public SegmentLoadResult TryLoad(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
            new() { Outcome = SegmentLoadOutcome.NotFound };

        public SegmentLoadResult TryLoad(
            GameplaySessionId gameplaySessionId,
            int segmentOrdinal,
            SegmentLoadOptions options) =>
            new() { Outcome = SegmentLoadOutcome.NotFound };

        public SegmentPublishedHeader ReadHeader(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
            new()
            {
                Status = SegmentHeaderReadStatus.NotFound,
                SegmentId = SegmentCaptureKey.Format(gameplaySessionId, segmentOrdinal)
            };

        public IReadOnlyList<SegmentPublishedHeader> ListHeaders() => [];
    }
}
