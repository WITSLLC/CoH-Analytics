using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class MonitoringSessionSameProcessAccountHandoffTests
{
    private static readonly DateTimeOffset StartA = new(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = new(2026, 8, 14, 2, 15, 39, TimeSpan.Zero);
    private static readonly string ExecutablePath =
        @"C:\Games\Homecoming\bin\win64\live\cityofheroes.exe";

    [Fact]
    public async Task Same_process_account_handoff_retires_TestAccount_and_starts_fresh_AltAccount_session()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, runtime, logActivity, time, gameplay, identity, viewed, parser) =
                await CreateHandoffStackAsync(processInstance, repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var primaryContext = Assert.Single(monitoring.Current.Contexts);
            Assert.Equal(processInstance, primaryContext.ProcessInstance);
            viewed.SelectGameplaySessionContext(primaryContext.ContextId);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    primaryContext.ContextId,
                    primarySource)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal)));

            time.Set(handoffAt);
            logActivity.Current = TestLogCandidates.Snapshot(
                revision: 2,
                handoffAt,
                TestLogCandidates.Create(
                    primarySource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: sessionStart),
                TestLogCandidates.Create(
                    altSource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: handoffAt));
            logActivity.RaiseActivityChanged();

            var altContext = Assert.Single(monitoring.Current.Contexts);
            Assert.Equal("AltAccount", altContext.AccountStableId);
            Assert.Equal(processInstance, altContext.ProcessInstance);
            Assert.DoesNotContain(
                monitoring.Current.Contexts,
                context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));

            Assert.DoesNotContain(
                identity.Current.Contexts,
                context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
            Assert.DoesNotContain(
                identity.Current.Contexts,
                context => string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal));

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => !gameplay.Current.Sessions.Any(session =>
                    string.Equals(session.AccountStableId, "TestAccount", StringComparison.Ordinal)));
            var provisionalSession = Assert.Single(gameplay.Current.Sessions);
            Assert.Equal(altContext.ContextId, provisionalSession.ContextId);
            Assert.Equal(GameplaySessionLifecycleState.Active, provisionalSession.LifecycleState);
            Assert.Null(provisionalSession.CharacterRecordId);
            Assert.Equal(0, provisionalSession.SessionExperienceGained);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:05:00 Welcome to City of Heroes, D4wn's Vanguard!",
                    altContext.ContextId,
                    altSource)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "D4wn's Vanguard", StringComparison.Ordinal)));

            var altSession = Assert.Single(gameplay.Current.Sessions);
            Assert.Equal(altContext.ContextId, altSession.ContextId);
            Assert.Equal(0, altSession.SessionExperienceGained);
            Assert.Equal("D4wn's Vanguard", altSession.CharacterDisplayName);

            Assert.NotEqual(primaryContext.ContextId, viewed.Current.LiveFollowContextId);
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
    public async Task Same_process_same_account_activity_does_not_handoff()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var later = sessionStart.AddMinutes(2);
        var (monitoring, _, logActivity, time, _, _, _, _) =
            await CreateHandoffStackAsync(processInstance);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var newerPrimarySource = TestLogCandidates.SourceId(
            "TestAccount",
            "TestAccount",
            logDate: new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
        logActivity.RaiseActivityChanged();

        var originalContextId = Assert.Single(monitoring.Current.Contexts).ContextId;

        time.Set(later);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            later,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Inactive, later, lastGrowthAt: sessionStart),
            TestLogCandidates.Create(
                newerPrimarySource,
                LogSourceActivityState.Growing,
                later,
                isRolloverCandidate: true,
                rolloverPredecessor: primarySource,
                lastGrowthAt: later));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal(originalContextId, context.ContextId);
        Assert.Equal("TestAccount", context.AccountStableId);
        Assert.Equal(newerPrimarySource, context.CurrentSourceId);
    }

    [Fact]
    public async Task Same_pid_with_different_start_time_does_not_same_process_handoff()
    {
        var originalClient = CreateClient(3_524, StartA);
        var reusedPidClient = CreateClient(3_524, StartB);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var (monitoring, runtime, logActivity, _, _, _, _, _) =
            await CreateHandoffStackAsync(originalClient);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
        logActivity.RaiseActivityChanged();

        var primaryContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal(originalClient, primaryContext.ProcessInstance);

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [reusedPidClient]);

        logActivity.RaiseActivityChanged();

        primaryContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal(reusedPidClient, primaryContext.ProcessInstance);
        Assert.Equal("TestAccount", primaryContext.AccountStableId);
    }

    [Fact]
    public async Task Two_simultaneous_processes_preserve_both_accounts_without_handoff()
    {
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var (monitoring, runtime, logActivity, _, gameplay, _, _, _) =
            await CreateHandoffStackAsync([CreateClient(3_524, StartA)]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
        logActivity.RaiseActivityChanged();
        var primaryContext = Assert.Single(monitoring.Current.Contexts);

        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart),
            TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, sessionStart));
        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [CreateClient(3_524, StartA), CreateClient(6_356, StartB)]);
        logActivity.RaiseActivityChanged();

        Assert.Equal(0, gameplay.ResetCount);
        Assert.Equal(2, monitoring.Current.Contexts.Count);
        Assert.Contains(monitoring.Current.Contexts, context => context.ContextId == primaryContext.ContextId);
        Assert.Contains(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "AltAccount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stale_TestAccount_log_does_not_resurrect_after_AltAccount_handoff()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var (monitoring, _, logActivity, time, _, _, _, _) =
            await CreateHandoffStackAsync(processInstance);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
        logActivity.RaiseActivityChanged();

        time.Set(handoffAt);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            handoffAt,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: sessionStart),
            TestLogCandidates.Create(
                altSource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: handoffAt));
        logActivity.RaiseActivityChanged();
        Assert.Equal("AltAccount", Assert.Single(monitoring.Current.Contexts).AccountStableId);

        var oscillationAt = handoffAt.AddMinutes(1);
        time.Set(oscillationAt);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 3,
            oscillationAt,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                oscillationAt,
                lastGrowthAt: sessionStart),
            TestLogCandidates.Create(
                altSource,
                LogSourceActivityState.Growing,
                oscillationAt,
                lastGrowthAt: handoffAt));
        logActivity.RaiseActivityChanged();

        Assert.Single(monitoring.Current.Contexts, context => context.AccountStableId == "AltAccount");
        Assert.DoesNotContain(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AltAccount_session_starts_with_zero_telemetry_after_handoff()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, _, logActivity, time, gameplay, identity, _, parser) =
                await CreateHandoffStackAsync(processInstance, repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var primaryContext = Assert.Single(monitoring.Current.Contexts);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    primaryContext.ContextId,
                    primarySource)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session => session.CharacterDisplayName == "Dawn's Vanguard"));

            time.Set(handoffAt);
            logActivity.Current = TestLogCandidates.Snapshot(
                revision: 2,
                handoffAt,
                TestLogCandidates.Create(
                    primarySource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: sessionStart),
                TestLogCandidates.Create(
                    altSource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: handoffAt));
            logActivity.RaiseActivityChanged();

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => !gameplay.Current.Sessions.Any(session =>
                    string.Equals(session.AccountStableId, "TestAccount", StringComparison.Ordinal)));

            var provisionalSession = Assert.Single(gameplay.Current.Sessions);
            Assert.Equal("AltAccount", provisionalSession.AccountStableId);
            Assert.Equal(GameplaySessionLifecycleState.Active, provisionalSession.LifecycleState);
            Assert.Equal(0, provisionalSession.SessionExperienceGained);
            Assert.DoesNotContain(
                identity.Current.Contexts,
                context => string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal));
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
    public async Task Old_character_identity_does_not_leak_into_AltAccount()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, _, logActivity, time, _, identity, _, parser) =
                await CreateHandoffStackAsync(processInstance, repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var primaryContext = Assert.Single(monitoring.Current.Contexts);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    primaryContext.ContextId,
                    primarySource)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal)));

            time.Set(handoffAt);
            logActivity.Current = TestLogCandidates.Snapshot(
                revision: 2,
                handoffAt,
                TestLogCandidates.Create(
                    primarySource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: sessionStart),
                TestLogCandidates.Create(
                    altSource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: handoffAt));
            logActivity.RaiseActivityChanged();

            var altContext = Assert.Single(identity.Current.Contexts);
            Assert.Equal("AltAccount", altContext.AccountStableId);
            Assert.Null(altContext.CharacterRecordId);
            Assert.NotEqual("Dawn's Vanguard", altContext.CharacterDisplayName);
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
    public async Task Viewed_live_follow_no_longer_targets_retired_TestAccount_context()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, _, logActivity, time, _, identity, viewed, parser) =
                await CreateHandoffStackAsync(processInstance, repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var primaryContext = Assert.Single(monitoring.Current.Contexts);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    primaryContext.ContextId,
                    primarySource)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal)));
            viewed.SelectGameplaySessionContext(primaryContext.ContextId);
            Assert.Equal(primaryContext.ContextId, viewed.Current.LiveFollowContextId);

            time.Set(handoffAt);
            logActivity.Current = TestLogCandidates.Snapshot(
                revision: 2,
                handoffAt,
                TestLogCandidates.Create(
                    primarySource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: sessionStart),
                TestLogCandidates.Create(
                    altSource,
                    LogSourceActivityState.Growing,
                    handoffAt,
                    lastGrowthAt: handoffAt));
            logActivity.RaiseActivityChanged();

            Assert.NotEqual(primaryContext.ContextId, viewed.Current.LiveFollowContextId);
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
    public async Task Unbound_TestAccount_from_ambiguous_multi_client_enrollment_is_retired_on_AltAccount_switch()
    {
        var soleClient = CreateClient(3_524, StartA);
        var secondClient = CreateClient(6_356, StartB);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClients = [soleClient, secondClient],
            RunningClientCount = 2
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(sessionStart);
        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
        await monitoring.StartAsync();
        logActivity.RaiseActivityChanged();

        var primaryContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal("TestAccount", primaryContext.AccountStableId);
        Assert.Null(primaryContext.ProcessInstance);

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [soleClient]);
        time.Set(handoffAt);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            handoffAt,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: sessionStart),
            TestLogCandidates.Create(
                altSource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: handoffAt));
        logActivity.RaiseActivityChanged();

        var altContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal("AltAccount", altContext.AccountStableId);
        Assert.Equal(soleClient, altContext.ProcessInstance);
        Assert.DoesNotContain(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Both_accounts_temporarily_growing_with_AltAccount_newer_enforces_single_account()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var (monitoring, _, logActivity, time, _, _, _, _) =
            await CreateHandoffStackAsync(processInstance);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
        logActivity.RaiseActivityChanged();

        var primaryContextId = Assert.Single(monitoring.Current.Contexts).ContextId;

        time.Set(handoffAt);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            handoffAt,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: handoffAt),
            TestLogCandidates.Create(
                altSource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: handoffAt.AddSeconds(1)));
        logActivity.RaiseActivityChanged();

        var altContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal("AltAccount", altContext.AccountStableId);
        Assert.NotEqual(primaryContextId, altContext.ContextId);
        Assert.DoesNotContain(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Continued_reconciliation_after_enforcement_keeps_TestAccount_retired()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(5);
        var (monitoring, _, logActivity, time, _, _, _, _) =
            await CreateHandoffStackAsync(processInstance);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
        logActivity.RaiseActivityChanged();

        time.Set(handoffAt);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            handoffAt,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: sessionStart),
            TestLogCandidates.Create(
                altSource,
                LogSourceActivityState.Growing,
                handoffAt,
                lastGrowthAt: handoffAt));
        logActivity.RaiseActivityChanged();
        Assert.Equal("AltAccount", Assert.Single(monitoring.Current.Contexts).AccountStableId);

        var oscillationAt = handoffAt.AddMinutes(2);
        time.Set(oscillationAt);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 3,
            oscillationAt,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                oscillationAt,
                lastGrowthAt: sessionStart),
            TestLogCandidates.Create(
                altSource,
                LogSourceActivityState.Growing,
                oscillationAt,
                lastGrowthAt: handoffAt));
        logActivity.RaiseActivityChanged();

        Assert.Single(monitoring.Current.Contexts, context => context.AccountStableId == "AltAccount");
        Assert.DoesNotContain(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_process_instances_do_not_globally_retire_either_account()
    {
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var (monitoring, runtime, logActivity, _, _, _, _, _) =
            await CreateHandoffStackAsync([CreateClient(3_524, StartA), CreateClient(6_356, StartB)]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart),
            TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, sessionStart));
        logActivity.RaiseActivityChanged();

        Assert.Equal(2, monitoring.Current.Contexts.Count);
        Assert.Contains(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
        Assert.Contains(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "AltAccount", StringComparison.Ordinal));
    }

    private static HomecomingProcessInstance CreateClient(int processId, DateTimeOffset processStartTime) =>
        FakeGameRuntimeService.CreateClient(processId, processStartTime, ExecutablePath);

    private static async Task<(
        MonitoringSessionManager Monitoring,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time,
        RecordingGameplaySessionManager Gameplay,
        GameplaySessionIdentityReadService Identity,
        ViewedContextService Viewed,
        GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser)> CreateHandoffStackAsync(
        HomecomingProcessInstance processInstance,
        CharacterRepository? repository = null)
    {
        var clients = new[] { processInstance };
        return await CreateHandoffStackAsync(clients, repository);
    }

    private static async Task<(
        MonitoringSessionManager Monitoring,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time,
        RecordingGameplaySessionManager Gameplay,
        GameplaySessionIdentityReadService Identity,
        ViewedContextService Viewed,
        GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser)> CreateHandoffStackAsync(
        IReadOnlyList<HomecomingProcessInstance> runningClients,
        CharacterRepository? repository = null)
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClients = runningClients,
            RunningClientCount = runningClients.Count
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        repository ??= GameplaySessionTestInfrastructure.CreateRepository(out _);
        var gameplayInner = new GameplaySessionManager(monitoring, parser, repository);
        var gameplay = new RecordingGameplaySessionManager(gameplayInner);
        var identity = new GameplaySessionIdentityReadService(gameplayInner, monitoring, repository);
        var viewed = new ViewedContextService(identity, repository);
        _ = new LiveRuntimeGenerationService(runtime, monitoring, gameplay, viewed);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClients);
        await monitoring.StartAsync();
        await gameplayInner.StartAsync();

        return (monitoring, runtime, logActivity, time, gameplay, identity, viewed, parser);
    }

    private sealed class RecordingGameplaySessionManager : IGameplaySessionManager
    {
        private readonly GameplaySessionManager _inner;

        public RecordingGameplaySessionManager(GameplaySessionManager inner) => _inner = inner;

        public int ResetCount { get; private set; }

        public GameplaySessionManagerSnapshot Current => _inner.Current;

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged
        {
            add => _inner.StateChanged += value;
            remove => _inner.StateChanged -= value;
        }

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable
        {
            add => _inner.CommittedEventsAvailable += value;
            remove => _inner.CommittedEventsAvailable -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => _inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) => _inner.StopAsync(cancellationToken);

        public GameplaySessionOperationResult ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId) =>
            _inner.ConfirmCharacter(contextId, characterRecordId);

        public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId) =>
            _inner.ClearIdentity(contextId);

        public GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId) =>
            _inner.StartTrackedCombat(contextId);

        public GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId) =>
            _inner.PauseTrackedCombat(contextId);

        public GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId) =>
            _inner.ResumeTrackedCombat(contextId);

        public GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId) =>
            _inner.StopTrackedCombat(contextId);

        public GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId) =>
            _inner.ResetTrackedCombat(contextId);

        public GameplaySessionDiagnostics GetDiagnostics() => _inner.GetDiagnostics();

        public void ResetForNewRuntimeGeneration()
        {
            ResetCount++;
            _inner.ResetForNewRuntimeGeneration();
        }
    }
}
