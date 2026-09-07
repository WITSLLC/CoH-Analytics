using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionRewardTelemetryTests
{
    [Fact]
    public async Task Recognized_currency_events_accumulate_session_totals()
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

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You received Reward Merit.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:12 You received 2 units of Reward Merit.",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Reward Merit"
                        && total.Quantity == 3)
                    && session.RecentRewards.Count == 2));
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
    public async Task Nightmare_obol_receipts_accumulate_session_totals()
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

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 04:43:59 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "[04:44] You received 15 units of Nightmare Obol.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "[04:45] You received 5 units of Nightmare Obol.",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Nightmare Obol"
                        && total.Quantity == 20)
                    && session.RecentRewards.Count == 2));
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
    public async Task Generic_received_item_is_preserved_in_recent_rewards()
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

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You received Impervium Armor.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RecentRewards.Any(entry =>
                        entry.Category == GameplaySessionRewardCategory.ReceivedItem
                        && entry.DisplayName == "Impervium Armor"
                        && entry.Quantity == 1)));
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
    public async Task Retained_pre_identity_reward_commits_exactly_once_on_confirmation()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            repository.EstablishTrustedFromWelcome("acct-1", "Example Hero");
            repository.EstablishTrustedFromManualConfirmation("acct-1", "Other Hero");

            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 You received Enhancement Converter.",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.RetainedEventCount == 1));

            var otherRecord = repository.TryFindTrustedByDisplayName("acct-1", "Other Hero")!;
            var confirm = manager.ConfirmCharacter(contextId, otherRecord.RecordId);
            Assert.True(confirm.IsSuccess);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RecentRewards.Count == 1
                    && session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Enhancement Converter"
                        && total.Quantity == 1)));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:02 You received Enhancement Converter.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RecentRewards.Count == 2
                    && session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Enhancement Converter"
                        && total.Quantity == 2)));
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
    public async Task Repeated_welcome_for_same_character_preserves_recent_rewards_and_totals()
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

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You received Reward Merit.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:20 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:21 You received Astral Merit.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RecentRewards.Count == 2
                    && session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Reward Merit"
                        && total.Quantity == 1)
                    && session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Astral Merit"
                        && total.Quantity == 1)));
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
    public async Task Recent_rewards_list_is_bounded()
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

            var options = new GameplaySessionOptions { MaxRecentSessionRewards = 3 };
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);

            var events = new List<ParserEvent>
            {
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1)
            };

            for (var index = 0; index < 5; index++)
            {
                events.Add(GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-04 06:27:{11 + index} You received Item-{index}.",
                    contextId,
                    source,
                    sequence: index + 2));
            }

            parser.PublishClassified(events);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RecentRewards.Count == 3
                    && session.RecentRewards[0].DisplayName == "Item-4"
                    && session.RecentRewards[2].DisplayName == "Item-2"));
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
    public async Task Multi_context_sessions_keep_isolated_reward_telemetry()
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

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Hero A!",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You received Reward Merit.",
                    contextA,
                    sourceA,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Hero B!",
                    contextB,
                    sourceB,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You received Impervium Armor.",
                    contextB,
                    sourceB,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var sessions = manager.Current.Sessions;
                return sessions.Count == 2
                    && sessions.Any(session =>
                        session.ContextId == contextA
                        && session.RecentRewards.Any(entry =>
                            entry.DisplayName == "Reward Merit")
                        && !session.RecentRewards.Any(entry =>
                            entry.DisplayName == "Impervium Armor"))
                    && sessions.Any(session =>
                        session.ContextId == contextB
                        && session.RecentRewards.Any(entry =>
                            entry.DisplayName == "Impervium Armor")
                        && !session.RecentRewards.Any(entry =>
                            entry.DisplayName == "Reward Merit"));
            });
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
}
