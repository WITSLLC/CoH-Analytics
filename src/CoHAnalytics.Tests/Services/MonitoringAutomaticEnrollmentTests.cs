using System.Collections.Concurrent;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// End-to-end cover for §3.6.6: a second unambiguous Homecoming session is monitored, parsed and
/// identified with no enrollment step, and without disturbing the session already running.
/// </summary>
public sealed class MonitoringAutomaticEnrollmentTests
{
    [Fact]
    public async Task Second_active_session_is_parsed_and_identified_without_enrollment()
    {
        const string welcomeA = "[03:57] Welcome to City of Heroes, Dawn's Vanguard!\r\n";
        const string welcomeB = "[03:58] Welcome to City of Heroes, D4wn's Vanguard!\r\n";

        using var directory = new ParserTestDirectory();
        var pathA = directory.CreateFile("alphaHero.log");
        var pathB = directory.CreateFile("altaccount.log");
        var sourceA = ParserTestSnapshots.Source(pathA, accountId: "TestAccount");
        var sourceB = ParserTestSnapshots.Source(pathB, accountId: "altaccount");

        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider();
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(sourceA, LogSourceActivityState.Growing, time.GetUtcNow()));

        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        await monitoring.StartAsync();
        var firstContext = Assert.Single(monitoring.Current.Contexts);

        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var classified = new ConcurrentQueue<ParserEvent>();
        parser.ClassifiedEventsAvailable += (_, args) =>
        {
            foreach (var parserEvent in args.Events)
            {
                classified.Enqueue(parserEvent);
            }
        };

        await parser.StartAsync();
        directory.Append(pathA, welcomeA);
        await ParserTestSnapshots.WaitUntilAsync(() => classified.Any(CharacterIdentityResolver.IsWelcomeEvidence));

        // A second client starts on its own account. Nothing asks the user to enroll it.
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            time.GetUtcNow(),
            TestLogCandidates.Create(sourceA, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(sourceB, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        Assert.Empty(monitoring.Current.PendingOffers);
        var secondContext = Assert.Single(monitoring.Current.Contexts, c => c.ContextId != firstContext.ContextId);
        Assert.Equal("altaccount", secondContext.AccountStableId);
        Assert.Equal(MonitoringContextState.Ready, secondContext.State);

        await ParserTestSnapshots.WaitUntilAsync(
            () => parser.Current.Workers.Any(worker => worker.ContextId == secondContext.ContextId));

        directory.Append(pathB, welcomeB);
        await ParserTestSnapshots.WaitUntilAsync(() => classified.Any(item =>
            item.ContextId == secondContext.ContextId && CharacterIdentityResolver.IsWelcomeEvidence(item)));

        var welcomes = classified.Where(CharacterIdentityResolver.IsWelcomeEvidence).ToArray();
        Assert.Contains(welcomes, item =>
            item.ContextId == firstContext.ContextId
            && CharacterIdentityResolver.GetStrongCandidateName(item) == "Dawn's Vanguard");
        Assert.Contains(welcomes, item =>
            item.ContextId == secondContext.ContextId
            && CharacterIdentityResolver.GetStrongCandidateName(item) == "D4wn's Vanguard");

        // Enrolling and parsing the second session never disturbs the first.
        var retained = Assert.Single(monitoring.Current.Contexts, c => c.ContextId == firstContext.ContextId);
        Assert.Equal(sourceA, retained.CurrentSourceId);
        Assert.Equal(MonitoringContextState.Ready, retained.State);
    }
}
