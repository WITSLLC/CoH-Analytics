using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class WelcomeCharacterResolutionRegressionTests
{
    private const string WelcomeBody = "Welcome to City of Heroes, Dawn's Vanguard!";
    private const string FullTimestampWelcomeLine = "2026-08-07 03:57:00 Welcome to City of Heroes, Dawn's Vanguard!";
    private const string BracketTimestampWelcomeLine = "[03:57] Welcome to City of Heroes, Dawn's Vanguard!";

    [Theory]
    [InlineData(FullTimestampWelcomeLine)]
    [InlineData(BracketTimestampWelcomeLine)]
    [InlineData(WelcomeBody)]
    public void Welcome_line_classifies_to_exact_character_name(string welcomeLine)
    {
        var contextId = MonitoringContextId.CreateNew();
        var source = TestAccountSource();
        var classified = GameplaySessionTestInfrastructure.Classify(
            welcomeLine,
            contextId,
            source);

        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, classified.EventKind);
        Assert.Equal("welcome_attribution", classified.ClassificationRuleId);
        Assert.True(CharacterIdentityResolver.IsWelcomeEvidence(classified));
        Assert.Equal("Dawn's Vanguard", CharacterIdentityResolver.GetStrongCandidateName(classified));
    }

    [Fact]
    public async Task Bracket_timestamp_welcome_resolves_confirmed_identity_for_account_and_context()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = TestAccountSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    BracketTimestampWelcomeLine,
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed
                    && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
                    && session.CharacterDisplayName == "Dawn's Vanguard"));

            var session = manager.Current.Sessions[0];
            Assert.Equal(contextId, session.ContextId);
            Assert.NotNull(session.CharacterRecordId);

            var record = repository.TryGetRecord(session.CharacterRecordId!);
            Assert.NotNull(record);
            Assert.Equal("TestAccount", record.AccountStableId);
            Assert.Equal("Dawn's Vanguard", record.CurrentDisplayName);
            Assert.Equal(CharacterTrustState.TrustedFromWelcome, record.TrustState);
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
    public async Task Bracket_timestamp_welcome_reuses_existing_trusted_character_record()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var existing = repository.EstablishTrustedFromWelcome("TestAccount", "Dawn's Vanguard");
            Assert.True(existing.IsSuccess);

            var contextId = MonitoringContextId.CreateNew();
            var source = TestAccountSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    BracketTimestampWelcomeLine,
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterRecordId == existing.RecordId));

            Assert.Equal(1, repository.Current.Records.Count(record =>
                record.AccountStableId == "TestAccount"
                && record.CurrentDisplayName == "Dawn's Vanguard"));
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
    public async Task Bracket_timestamp_welcome_creates_trusted_record_for_new_character()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = TestAccountSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "[08:15] Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterDisplayName == "Example Hero"
                    && session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed));

            var record = repository.TryFindTrustedByDisplayName("TestAccount", "Example Hero");
            Assert.NotNull(record);
            Assert.Equal(CharacterTrustState.TrustedFromWelcome, record.TrustState);
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

    private static LogSourceId TestAccountSource() =>
        LogSourceId.Create(
            "TestAccount",
            "TestAccount",
            @"C:\fake\TestAccount\Logs\chatlog 2026-08-04.txt",
            new DateOnly(2026, 8, 4));
}
