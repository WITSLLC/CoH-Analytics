using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class Slice6LiveCombatPathTests
{
    [Fact]
    public async Task Canonical_only_burst_obeys_existing_combat_publication_cadence()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        long publications = 0;
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(monitoring, parser, repository,
            new GameplaySessionOptions { TimeProvider = time, CombatSnapshotPublishInterval = TimeSpan.FromSeconds(1),
                TestHooks = new GameplaySessionTestHooks { OnSnapshotPublished = () => Interlocked.Increment(ref publications) } });
        try
        {
            parser.PublishClassified([GameplaySessionTestInfrastructure.Classify(
                "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!", context, source, 1)]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Interlocked.Exchange(ref publications, 0);
            for (var i = 2; i < 102; i++)
                parser.PublishClassified([GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 Fire Cages is still recharging.", context, source, i)]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Assert.Equal(0, Volatile.Read(ref publications));
            time.Advance(TimeSpan.FromSeconds(1));
            parser.PublishClassified([GameplaySessionTestInfrastructure.Classify(
                "2026-08-04 12:00:02 Fire Cages is still recharging.", context, source, 102)]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var snapshot = Assert.Single(manager.Current.Sessions);
            Assert.Equal(101, snapshot.CombatAnalytics.LogicalEventsApplied);
            Assert.Equal(101, snapshot.CombatAnalytics.Session.UnmatchedRechargeCandidateCount);
            Assert.Equal(1, Volatile.Read(ref publications));
            Assert.Equal(0, snapshot.Combat.PowerActivations);
            await manager.StopAsync();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("Hero A", "Hero B", "Hero A")]
    [InlineData("Hero A", "Hero A", "Hero A")]
    public async Task Welcome_boundaries_reset_analytics_lifecycle_and_reprocessing(string first, string second, string third)
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(monitoring, parser, repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero });
        ParserEvent Line(string body, long seq) => GameplaySessionTestInfrastructure.Classify(
            "2026-08-04 12:00:00 " + body, context, source, seq) with
            { SourceSegmentId = segment, SourceByteStart = seq * 300, SourceByteEnd = seq * 300 + 200 };
        try
        {
            async Task Send(ParserEvent item)
            {
                parser.PublishClassified([item]);
                await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            }
            await Send(Line($"Welcome to City of Heroes, {first}!", 1));
            var firstId = Assert.Single(manager.Current.Sessions).SessionId;
            await Send(Line("You activated the Hasten power.", 2));
            var damage = Line("Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage.", 3);
            await Send(damage);
            await Send(damage);
            await Send(Line("Hasten is recharged.", 4));
            var initial = Assert.Single(manager.Current.Sessions, item => item.SessionId == firstId);
            Assert.Equal(3, initial.CombatAnalytics.LogicalEventsApplied);
            Assert.Equal(1, initial.CombatAnalytics.Session.ConfirmedRechargeCompletedCount);
            await Send(Line($"Welcome to City of Heroes, {second}!", 5));
            await Send(Line("Hasten is recharged.", 6));
            var secondSession = Assert.Single(manager.Current.Sessions, item => item.FinalizedAt is null);
            Assert.NotEqual(firstId, secondSession.SessionId);
            Assert.Equal(0, secondSession.CombatAnalytics.Session.ConfirmedRechargeCompletedCount);
            Assert.Equal(1, secondSession.CombatAnalytics.Session.UnmatchedRechargeCandidateCount);
            Assert.Empty(secondSession.CombatAnalytics.Powers);
            Assert.Empty(secondSession.CombatAnalytics.Targets);
            await Send(Line($"Welcome to City of Heroes, {third}!", 7));
            await Send(Line("Hasten is still recharging.", 8));
            var last = Assert.Single(manager.Current.Sessions, item => item.FinalizedAt is null);
            Assert.Equal(0, last.CombatAnalytics.Session.ConfirmedStillRechargingCount);
            Assert.Equal(1, last.CombatAnalytics.Session.UnmatchedRechargeCandidateCount);
            Assert.Equal(1561, initial.CombatAnalytics.Session.DamageDealt.Hundredths);
            await manager.StopAsync();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Test_only_mirror_rule_dedups_separate_live_calls_and_does_not_cross_sessions()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = new GameplaySessionManager(monitoring, parser, repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
            combatEventParser: new TestChannelParser())
        {
            CombatMirrorPolicy = MirrorCompatibilityPolicy.ForTests(new MirrorCompatibilityRule
            {
                RuleId = "test-live-heal", LeftFamily = CombatEventFamily.HealDealt,
                RightFamily = CombatEventFamily.HealReceived, LeftChannel = "Delivery", RightChannel = "Receipt",
                RequiredDiscriminator = MirrorDiscriminatorKind.ActualSourceChannel,
                FacetUnion = EventFacets.HealDelivered | EventFacets.HealReceived
            })
        };
        try
        {
            await manager.StartAsync();
            async Task Send(string body, long seq)
            {
                parser.PublishClassified([GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 " + body, context, source, seq) with
                    { SourceSegmentId = segment, SourceByteStart = seq * 300, SourceByteEnd = seq * 300 + 200 }]);
                await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            }
            await Send("Welcome to City of Heroes, Hero A!", 1);
            await Send("Hero_A heals you with their Transfusion for 421.34 health points.", 2);
            await Send("You heal Hero_A with Transfusion for 421.34 health points.", 3);
            await Send("You activate Hasten.", 25); // Closes the prior mirror candidate window.
            var first = Assert.Single(manager.Current.Sessions, item => item.CharacterDisplayName == "Hero A");
            Assert.Equal(1, first.CombatAnalytics.LogicalEventsApplied);
            Assert.Equal(42134, first.CombatAnalytics.Session.HealingDealt.Hundredths);
            Assert.Equal(42134, first.CombatAnalytics.Session.HealingReceived.Hundredths);
            Assert.Equal(first.Combat.HealingDealt, first.CombatAnalytics.Session.HealingDealt);
            Assert.Equal(first.Combat.HealingReceived, first.CombatAnalytics.Session.HealingReceived);
            await Send("Welcome to City of Heroes, Hero B!", 26);
            await Send("Hero_A heals you with their Transfusion for 421.34 health points.", 27);
            await Send("You activate Hasten.", 50);
            var second = Assert.Single(manager.Current.Sessions, item => item.CharacterDisplayName == "Hero B");
            Assert.Equal(1, second.CombatAnalytics.LogicalEventsApplied);
            Assert.Equal(0, second.CombatAnalytics.Session.HealingDealt.Hundredths);
            Assert.Equal(42134, second.CombatAnalytics.Session.HealingReceived.Hundredths);
            await manager.StopAsync();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class TestChannelParser : ICombatEventParser
    {
        private readonly CombatEventParser _parser = new();
        public bool TryParse(ParserEvent input, out CombatEvent item) => _parser.TryParse(input, out item);
        public bool TryAdaptToLegacy(CanonicalCombatEvent item, out CombatEvent legacy) => _parser.TryAdaptToLegacy(item, out legacy);
        public bool TryParseCanonical(ParserEvent input, out CanonicalCombatEvent item)
        {
            if (!_parser.TryParseCanonical(input, out item)) return false;
            var channel = item.Family == CombatEventFamily.HealDealt ? "Delivery" : "Receipt";
            item = item with { SourceChannel = channel, Provenance = item.Provenance with { SourceChannel = channel },
                MirrorClass = item.MirrorClass with { SourceChannel = channel } };
            return true;
        }
    }

    [Fact]
    public async Task Live_path_canonical_event_reaches_combat_engine_exactly_once()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

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
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.Zero
                });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId && session.CharacterDisplayName == "Hero A"));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CombatAnalytics.LogicalEventsApplied == 1
                    && session.CombatAnalytics.Session.DamageDealt == new CombatScaledAmount(1388)));

            var session = Assert.Single(manager.Current.Sessions, item => item.ContextId == contextId);
            Assert.Equal(1, session.CombatAnalytics.LogicalEventsApplied);
            Assert.Equal(0, session.CombatAnalytics.DuplicateOccurrencesIgnored);
            Assert.Equal(new CombatScaledAmount(1388), session.Combat.DamageDealt);
            Assert.Equal(session.Combat.DamageDealt, session.CombatAnalytics.Session.DamageDealtSelf);
            Assert.Equal(AnalyticsSemanticVersion.Current, session.CombatAnalytics.AnalyticsSemanticVersion);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Live_path_includes_owned_pet_in_engine_totals_not_legacy_snapshot()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

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
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.Zero
                });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId && session.CharacterDisplayName == "Hero A"));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:02 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CombatAnalytics.LogicalEventsApplied == 2));

            var session = Assert.Single(manager.Current.Sessions, item => item.ContextId == contextId);
            Assert.Equal(new CombatScaledAmount(1388), session.Combat.DamageDealt);
            Assert.Equal(new CombatScaledAmount(1388), session.CombatAnalytics.Session.DamageDealtSelf);
            Assert.Equal(new CombatScaledAmount(1561), session.CombatAnalytics.Session.DamageDealtOwnedPets);
            Assert.Equal(new CombatScaledAmount(1388 + 1561), session.CombatAnalytics.Session.DamageDealt);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
