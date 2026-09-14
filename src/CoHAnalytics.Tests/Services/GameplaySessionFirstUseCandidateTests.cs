using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionFirstUseCandidateTests
{
    [Fact]
    public async Task Repeated_reciprocal_panacea_evidence_surfaces_candidate_without_trust()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hell's Vengence", "13:42:33", 1));
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hell's Vengence", "13:43:03", 3));

        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            harness.Manager.Current.Sessions.Any(session => session.CandidateCount == 1));

        var session = Assert.Single(harness.Manager.Current.Sessions);
        Assert.Equal(CharacterIdentityConfidence.Unknown, session.CharacterIdentityConfidence);
        Assert.Equal(CharacterIdentityResolutionState.Candidate, session.CharacterIdentityResolutionState);
        Assert.Equal("Hell's Vengence", Assert.Single(session.IdentityCandidates).DisplayName);
        Assert.Null(harness.Repository.TryFindTrustedByDisplayName("acct-1", "Hell's Vengence"));
        Assert.All(
            session.IdentityCandidates,
            candidate => Assert.Equal(1, candidate.ObservationCount));
    }

    [Fact]
    public async Task Single_reciprocal_pair_is_not_enough_to_surface_a_candidate()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hell's Vengence", "13:43:03", 1));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);

        var session = Assert.Single(harness.Manager.Current.Sessions);
        Assert.Equal(0, session.CandidateCount);
        Assert.Equal(CharacterIdentityResolutionState.Unresolved, session.CharacterIdentityResolutionState);
    }

    [Fact]
    public async Task Generic_hits_you_line_does_not_create_a_candidate()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, [
            Line(harness, "13:42:33 Example Hero hits you with their effect.", 1),
            Line(harness, "13:42:34 Example Hero hits you with their effect.", 2)
        ]);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);

        var session = Assert.Single(harness.Manager.Current.Sessions);
        Assert.Equal(0, session.CandidateCount);
        Assert.Equal(CharacterIdentityResolutionState.Unresolved, session.CharacterIdentityResolutionState);
        Assert.False(CharacterIdentityResolver.IsStrongAttributedEvidence(
            GameplaySessionTestInfrastructure.Classify(
                "2026-07-30 13:42:33 Example Hero hits you with their effect.",
                harness.ContextId,
                harness.Source)));
    }

    [Fact]
    public async Task One_sided_autohit_and_ally_pet_dummy_targets_do_not_create_candidates()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, [
            Line(harness, "13:43:43 HIT Ally! Your Speed Boost power is autohit.", 1),
            Line(harness, "13:43:44 HIT Imp! Your Empowering Burst power is autohit.", 2),
            Line(harness, "13:43:45 HIT Training Dummy! Your Siphon Power power is autohit.", 3),
            Line(harness, "13:43:46 HIT Hell's Vengence! Your Hasten power is autohit.", 4)
        ]);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);

        Assert.Equal(0, Assert.Single(harness.Manager.Current.Sessions).CandidateCount);
    }

    [Fact]
    public async Task Chat_only_evidence_does_not_create_a_candidate()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, [
            Line(harness, "13:43:10 [Team] Hell's Vengence: testing chat log capture", 1),
            Line(harness, "13:43:11 [Team] Hell's Vengence: testing chat log capture", 2),
            Line(harness, "13:43:12 [Team] Hell's Vengence: testing chat log capture", 3)
        ]);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);

        Assert.Equal(0, Assert.Single(harness.Manager.Current.Sessions).CandidateCount);
    }

    [Fact]
    public async Task Empty_repository_still_exposes_log_derived_picker_options()
    {
        await using var harness = await Harness.CreateAsync();
        using var identity = new GameplaySessionIdentityReadService(
            harness.Manager,
            harness.Monitoring,
            harness.Repository);

        Publish(harness, ReciprocalPanaceaGrant(harness, "Hell's Vengence", "13:42:33", 1));
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hell's Vengence", "13:43:03", 3));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            identity.Current.Contexts.Any(context =>
                context.RequiresManualSelection && context.PickerCharacters.Count == 1));

        var read = Assert.Single(identity.Current.Contexts);
        Assert.True(read.RequiresManualSelection);
        Assert.Equal(CharacterIdentityResolutionState.Candidate, read.CharacterIdentityResolutionState);
        var option = Assert.Single(read.PickerCharacters);
        Assert.Equal("Hell's Vengence", option.DisplayName);
        Assert.Null(option.RecordId);
        Assert.Null(harness.Repository.TryFindTrustedByDisplayName("acct-1", "Hell's Vengence"));
    }

    [Fact]
    public async Task Confirming_first_use_candidate_establishes_manual_trust_without_welcome_boundary()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hell's Vengence", "13:42:33", 1));
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hell's Vengence", "13:43:03", 3));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            harness.Manager.Current.Sessions.Any(session => session.CandidateCount == 1));

        var sessionId = Assert.Single(harness.Manager.Current.Sessions).SessionId;
        var establish = harness.Repository.EstablishTrustedFromManualConfirmation("acct-1", "Hell's Vengence");
        Assert.True(establish.IsSuccess);
        var confirm = harness.Manager.ConfirmCharacter(harness.ContextId, establish.RecordId!);
        Assert.True(confirm.IsSuccess);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);

        var session = Assert.Single(harness.Manager.Current.Sessions);
        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
        Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
        Assert.Equal("Hell's Vengence", session.CharacterDisplayName);
        Assert.Equal(
            CharacterTrustState.TrustedFromManualConfirmation,
            harness.Repository.TryGetRecord(establish.RecordId!)!.TrustState);
        Assert.DoesNotContain(
            harness.Manager.GetDiagnostics().RecentOperations,
            operation => operation.Contains("Welcome boundary", StringComparison.Ordinal));
        Assert.Contains(
            harness.Manager.GetDiagnostics().RecentOperations,
            operation => operation.Contains("Manual confirmation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_credible_names_are_conflicted_and_are_not_auto_trusted()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hero Alpha", "13:42:33", 1));
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hero Alpha", "13:42:34", 3));
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hero Beta", "13:42:35", 5));
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hero Beta", "13:42:36", 7));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            harness.Manager.Current.Sessions.Any(session =>
                session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Conflicted
                && session.CandidateCount == 2));

        var session = Assert.Single(harness.Manager.Current.Sessions);
        Assert.Equal(CharacterIdentityConfidence.Unknown, session.CharacterIdentityConfidence);
        Assert.Equal(2, session.CandidateCount);
        Assert.Null(harness.Repository.TryFindTrustedByDisplayName("acct-1", "Hero Alpha"));
        Assert.Null(harness.Repository.TryFindTrustedByDisplayName("acct-1", "Hero Beta"));

        using var identity = new GameplaySessionIdentityReadService(
            harness.Manager,
            harness.Monitoring,
            harness.Repository);
        var picker = identity.Current.Contexts.Single().PickerCharacters;
        Assert.Equal(2, picker.Count);
        Assert.All(picker, option => Assert.Null(option.RecordId));
    }

    [Fact]
    public async Task Later_welcome_still_wins_through_the_trusted_path()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hell's Vengence", "13:42:33", 1));
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hell's Vengence", "13:43:03", 3));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            harness.Manager.Current.Sessions.Any(session => session.CandidateCount == 1));

        var candidateSessionId = Assert.Single(harness.Manager.Current.Sessions).SessionId;
        Publish(harness, [Line(harness, "15:30:02 Welcome to City of Heroes, Hell's Vengence!", 5)]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            harness.Manager.Current.Sessions.Any(session =>
                session.LifecycleState == GameplaySessionLifecycleState.Active
                && session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed));

        var active = harness.Manager.Current.Sessions.Single(session =>
            session.LifecycleState == GameplaySessionLifecycleState.Active);
        Assert.NotEqual(candidateSessionId, active.SessionId);
        Assert.Equal("Hell's Vengence", active.CharacterDisplayName);
        Assert.Equal(CharacterIdentityResolutionState.Resolved, active.CharacterIdentityResolutionState);
        Assert.Equal(
            CharacterTrustState.TrustedFromWelcome,
            harness.Repository.TryFindTrustedByDisplayName("acct-1", "Hell's Vengence")!.TrustState);
        Assert.Contains(
            harness.Manager.GetDiagnostics().RecentOperations,
            operation => operation.Contains("Welcome boundary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Existing_trusted_record_picker_still_appears_without_reciprocal_evidence()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Repository.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");
        using var identity = new GameplaySessionIdentityReadService(
            harness.Manager,
            harness.Monitoring,
            harness.Repository);

        Publish(harness, [Line(harness, "13:58:00 You gain 10 experience.", 1)]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            identity.Current.Contexts.Any(context => context.RequiresManualSelection));

        var read = Assert.Single(identity.Current.Contexts);
        Assert.Equal(CharacterIdentityResolutionState.Unresolved, read.CharacterIdentityResolutionState);
        Assert.Equal(0, read.CandidateCount);
        var option = Assert.Single(read.PickerCharacters);
        Assert.Equal("Dawn's Vanguard", option.DisplayName);
        Assert.NotNull(option.RecordId);
    }

    [Fact]
    public async Task Sanitized_fixture_surfaces_candidate_then_real_welcome_remains_authoritative()
    {
        await using var harness = await Harness.CreateAsync();
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Services",
            "Fixtures",
            "first-use-candidate-2026-07-30.log");
        var lines = File.ReadAllLines(path)
            .Where(line => !line.Contains("Welcome to City of Heroes", StringComparison.Ordinal))
            .ToArray();

        var events = new List<ParserEvent>();
        for (var index = 0; index < lines.Length; index++)
        {
            events.Add(GameplaySessionTestInfrastructure.Classify(
                lines[index],
                harness.ContextId,
                harness.Source,
                sequence: index + 1) with { SourceSegmentId = harness.SegmentId });
        }

        harness.Parser.PublishClassified(events);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            harness.Manager.Current.Sessions.Any(session => session.CandidateCount == 1));

        var session = Assert.Single(harness.Manager.Current.Sessions);
        Assert.Equal("Hell's Vengence", Assert.Single(session.IdentityCandidates).DisplayName);
        Assert.Equal(CharacterIdentityResolutionState.Candidate, session.CharacterIdentityResolutionState);
        Assert.Null(harness.Repository.TryFindTrustedByDisplayName("acct-1", "Hell's Vengence"));

        Publish(harness, [Line(harness, File.ReadAllLines(path).Single(line =>
            line.Contains("Welcome to City of Heroes", StringComparison.Ordinal)), lines.Length + 1)]);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        var active = harness.Manager.Current.Sessions.Single(item =>
            item.LifecycleState == GameplaySessionLifecycleState.Active);
        Assert.NotEqual(session.SessionId, active.SessionId);
        Assert.Equal(CharacterIdentityConfidence.Confirmed, active.CharacterIdentityConfidence);
        Assert.Equal(CharacterTrustState.TrustedFromWelcome,
            harness.Repository.TryFindTrustedByDisplayName("acct-1", "Hell's Vengence")!.TrustState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parser_reset_discards_pending_halves_and_completed_pair_counts(bool completePair)
    {
        await using var harness = await Harness.CreateAsync();
        var pair = ReciprocalPanaceaHeal(harness, "Hero Alpha", "13:42:33", 1);
        Publish(harness, completePair ? pair : [pair[0]]);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);

        harness.SegmentId = ParserSourceSegmentId.CreateNew();
        Publish(harness, [Line(harness, "13:42:33 You heal Hero Alpha with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.", 1)]);
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hero Alpha", "13:42:34", 2));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        Assert.Equal(0, Assert.Single(harness.Manager.Current.Sessions).CandidateCount);
    }

    [Fact]
    public async Task Account_change_discards_candidate_evidence()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hero Alpha", "13:42:33", 1));
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hero Alpha", "13:42:34", 3));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        Assert.Equal(1, Assert.Single(harness.Manager.Current.Sessions).CandidateCount);

        harness.Monitoring.Publish(ParserTestSnapshots.Snapshot(2,
            GameplaySessionTestInfrastructure.ReadyContext(harness.ContextId, harness.Source) with
            { AccountStableId = "acct-2" }));
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hero Alpha", "13:42:35", 5));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        var session = Assert.Single(harness.Manager.Current.Sessions);
        Assert.Equal(0, session.CandidateCount);
        Assert.Equal(CharacterIdentityResolutionState.Unresolved, session.CharacterIdentityResolutionState);
    }

    [Theory]
    [InlineData(2, "PANACEA: Chance for +Hit Points/Endurance", "Hero Alpha", 1)]
    [InlineData(3, "Panacea: Chance for +Hit Points/Endurance", "Hero Alpha", 0)]
    [InlineData(1, "Different Power", "Hero Alpha", 0)]
    [InlineData(1, "Panacea: Chance for +Hit Points/Endurance", "Hero Beta", 0)]
    public async Task Pair_threshold_requires_matching_name_power_and_two_second_window(
        int seconds, string power, string name, int expectedCandidates)
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hero Alpha", "13:42:30", 1));
        Publish(harness, [
            Line(harness, "13:42:33 Hero Alpha heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.", 3),
            Line(harness, $"13:42:{33 + seconds} You heal {name} with {power} for 78.93 health points.", 4)
        ]);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        Assert.Equal(expectedCandidates, Assert.Single(harness.Manager.Current.Sessions).CandidateCount);
    }

    [Fact]
    public async Task Reciprocal_pair_counts_do_not_cross_runtime_sessions()
    {
        await using var harness = await Harness.CreateAsync();
        Publish(harness, ReciprocalPanaceaHeal(harness, "Hero Alpha", "13:42:33", 1));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        var originalId = Assert.Single(harness.Manager.Current.Sessions).SessionId;
        harness.Manager.ResetForNewRuntimeGeneration();
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hero Alpha", "13:42:34", 3));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        var active = harness.Manager.Current.Sessions.Single(session =>
            session.LifecycleState == GameplaySessionLifecycleState.Active);
        Assert.NotEqual(originalId, active.SessionId);
        Assert.Equal(0, active.CandidateCount);
    }

    [Fact]
    public async Task Reciprocal_halves_do_not_cross_contexts()
    {
        await using var harness = await Harness.CreateAsync();
        var otherContextId = MonitoringContextId.CreateNew();
        harness.Monitoring.Publish(ParserTestSnapshots.Snapshot(2,
            GameplaySessionTestInfrastructure.ReadyContext(harness.ContextId, harness.Source),
            GameplaySessionTestInfrastructure.ReadyContext(otherContextId, harness.Source)));
        var pair = ReciprocalPanaceaHeal(harness, "Hero Alpha", "13:42:33", 1);
        Publish(harness, [pair[0], pair[1] with { ContextId = otherContextId }]);
        Publish(harness, ReciprocalPanaceaGrant(harness, "Hero Alpha", "13:42:34", 3));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(harness.Manager);
        Assert.Equal(2, harness.Manager.Current.Sessions.Count);
        Assert.All(harness.Manager.Current.Sessions, session => Assert.Equal(0, session.CandidateCount));
    }

    private static void Publish(Harness harness, IReadOnlyList<ParserEvent> events) =>
        harness.Parser.PublishClassified(events);

    private static ParserEvent[] ReciprocalPanaceaHeal(
        Harness harness,
        string name,
        string time,
        long sequence) =>
    [
        Line(harness, $"{time} {name} heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.", sequence),
        Line(harness, $"{time} You heal {name} with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.", sequence + 1)
    ];

    private static ParserEvent[] ReciprocalPanaceaGrant(
        Harness harness,
        string name,
        string time,
        long sequence) =>
    [
        Line(harness, $"{time} {name} hits you with their Panacea: Chance for +Hit Points/Endurance granting you 7.67 points of endurance.", sequence),
        Line(harness, $"{time} You hit {name} with your Panacea: Chance for +Hit Points/Endurance granting them 7.67 points of endurance.", sequence + 1)
    ];

    private static ParserEvent Line(Harness harness, string bodyWithoutDate, long sequence) =>
        GameplaySessionTestInfrastructure.Classify(
            bodyWithoutDate.StartsWith("2026-", StringComparison.Ordinal)
                ? bodyWithoutDate
                : $"2026-07-30 {bodyWithoutDate}",
            harness.ContextId,
            harness.Source,
            sequence) with { SourceSegmentId = harness.SegmentId };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _dataDirectory;

        private Harness(
            FakeMonitoringSessionManager monitoring,
            GameplaySessionTestInfrastructure.FakeGameplayParserManager parser,
            CharacterRepository repository,
            GameplaySessionManager manager,
            MonitoringContextId contextId,
            LogSourceId source,
            string dataDirectory)
        {
            Monitoring = monitoring;
            Parser = parser;
            Repository = repository;
            Manager = manager;
            ContextId = contextId;
            Source = source;
            _dataDirectory = dataDirectory;
        }

        public FakeMonitoringSessionManager Monitoring { get; }

        public GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser { get; }

        public CharacterRepository Repository { get; }

        public GameplaySessionManager Manager { get; }

        public MonitoringContextId ContextId { get; }

        public LogSourceId Source { get; }

        public ParserSourceSegmentId SegmentId { get; set; } = ParserSourceSegmentId.CreateNew();

        public static async Task<Harness> CreateAsync()
        {
            var monitoring = new FakeMonitoringSessionManager();
            var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
            var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);
            return new Harness(monitoring, parser, repository, manager, contextId, source, dir);
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.StopAsync();
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }
}
