using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ViewedContextServiceTests
{
    [Fact]
    public void Single_confirmed_live_context_updates_viewed_context_when_following_live()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var identity = new FakeGameplaySessionIdentityReadService();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = CreateDataDirectory()
        });
        repository.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");

        identity.Current = CreateSnapshot(contextId, "acct-1", recordId, "Dawn's Vanguard");
        var service = new ViewedContextService(identity, repository);

        Assert.True(service.Current.IsFollowingLive);
        Assert.Equal("acct-1", service.Current.AccountStableId);
        Assert.Equal(recordId, service.Current.CharacterRecordId);
        Assert.Equal(contextId, service.Current.LiveFollowContextId);
    }

    [Fact]
    public void Explicit_character_selection_stops_live_follow()
    {
        var contextId = MonitoringContextId.CreateNew();
        var liveRecordId = CharacterRecordId.CreateNew();
        var browseRecordId = CharacterRecordId.CreateNew();
        var identity = new FakeGameplaySessionIdentityReadService();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = CreateDataDirectory()
        });
        repository.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");
        repository.EstablishTrustedFromWelcome("acct-1", "Fire Farmer");

        identity.Current = CreateSnapshot(contextId, "acct-1", liveRecordId, "Dawn's Vanguard");
        var service = new ViewedContextService(identity, repository);

        service.SelectViewedCharacter("acct-1", browseRecordId);

        Assert.False(service.Current.IsFollowingLive);
        Assert.Equal(browseRecordId, service.Current.CharacterRecordId);

        identity.Current = CreateSnapshot(contextId, "acct-1", liveRecordId, "Dawn's Vanguard");
        identity.RaiseChanged();

        Assert.Equal(browseRecordId, service.Current.CharacterRecordId);
        Assert.False(service.Current.IsFollowingLive);
    }

    [Fact]
    public void Return_to_live_restores_live_follow_target()
    {
        var contextId = MonitoringContextId.CreateNew();
        var liveRecordId = CharacterRecordId.CreateNew();
        var browseRecordId = CharacterRecordId.CreateNew();
        var identity = new FakeGameplaySessionIdentityReadService();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = CreateDataDirectory()
        });

        identity.Current = CreateSnapshot(contextId, "acct-1", liveRecordId, "Dawn's Vanguard");
        var service = new ViewedContextService(identity, repository);
        service.SelectViewedCharacter("acct-1", browseRecordId);

        service.ReturnToLive();

        Assert.True(service.Current.IsFollowingLive);
        Assert.Equal(liveRecordId, service.Current.CharacterRecordId);
        Assert.Equal(contextId, service.Current.LiveFollowContextId);
    }

    [Fact]
    public void Multiple_live_contexts_preserve_existing_follow_target()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var identity = new FakeGameplaySessionIdentityReadService();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = CreateDataDirectory()
        });

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [
                CreateContext(contextA, "acct-1", recordA, "Dawn's Vanguard"),
                CreateContext(contextB, "acct-2", recordB, "Fire Farmer")
            ],
            DateTimeOffset.UtcNow,
            1);

        var service = new ViewedContextService(identity, repository);
        Assert.True(service.Current.IsFollowingLive);
        Assert.False(service.Current.HasCharacter);

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [CreateContext(contextA, "acct-1", recordA, "Dawn's Vanguard")],
            DateTimeOffset.UtcNow,
            2);
        identity.RaiseChanged();

        Assert.True(service.Current.IsFollowingLive);
        Assert.Equal(recordA, service.Current.CharacterRecordId);
        Assert.Equal(contextA, service.Current.LiveFollowContextId);

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [
                CreateContext(contextA, "acct-1", recordA, "Dawn's Vanguard"),
                CreateContext(contextB, "acct-2", recordB, "Fire Farmer")
            ],
            DateTimeOffset.UtcNow,
            3);
        identity.RaiseChanged();

        Assert.Equal(recordA, service.Current.CharacterRecordId);
        Assert.Equal(contextA, service.Current.LiveFollowContextId);
    }

  [Fact]
    public void Multiple_live_contexts_do_not_change_viewed_context_without_follow_target()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var browseRecordId = CharacterRecordId.CreateNew();
        var identity = new FakeGameplaySessionIdentityReadService();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = CreateDataDirectory()
        });

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [
                CreateContext(contextA, "acct-1", recordA, "Dawn's Vanguard"),
                CreateContext(contextB, "acct-2", recordB, "Fire Farmer")
            ],
            DateTimeOffset.UtcNow,
            1);

        var service = new ViewedContextService(identity, repository);
        service.SelectViewedCharacter("acct-1", browseRecordId);

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [
                CreateContext(contextB, "acct-2", recordB, "Fire Farmer")
            ],
            DateTimeOffset.UtcNow,
            2);
        identity.RaiseChanged();

        Assert.Equal(browseRecordId, service.Current.CharacterRecordId);
        Assert.False(service.Current.IsFollowingLive);
    }

    [Fact]
    public void Select_gameplay_session_context_updates_live_follow_target()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var identity = new FakeGameplaySessionIdentityReadService();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = CreateDataDirectory()
        });

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [
                CreateContext(contextA, "acct-1", recordA, "Dawn's Vanguard"),
                CreateContext(contextB, "acct-2", recordB, "Fire Farmer")
            ],
            DateTimeOffset.UtcNow,
            1);

        var service = new ViewedContextService(identity, repository);
        service.SelectGameplaySessionContext(contextB);

        Assert.True(service.Current.IsFollowingLive);
        Assert.Equal(contextB, service.Current.LiveFollowContextId);
        Assert.Equal(recordB, service.Current.CharacterRecordId);
    }

    [Fact]
    public void First_session_remains_selected_when_additional_sessions_start()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var identity = new FakeGameplaySessionIdentityReadService();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = CreateDataDirectory()
        });

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [CreateContext(contextA, "acct-1", recordA, "Dawn's Vanguard")],
            DateTimeOffset.UtcNow,
            1);
        var service = new ViewedContextService(identity, repository);
        Assert.Equal(contextA, service.Current.LiveFollowContextId);

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            [
                CreateContext(contextA, "acct-1", recordA, "Dawn's Vanguard"),
                CreateContext(contextB, "acct-2", recordB, "Fire Farmer")
            ],
            DateTimeOffset.UtcNow,
            2);
        identity.RaiseChanged();

        Assert.Equal(contextA, service.Current.LiveFollowContextId);
    }

    private static string CreateDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "coh-analytics-viewed-context", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static GameplaySessionIdentityReadModelSnapshot CreateSnapshot(
        MonitoringContextId contextId,
        string accountStableId,
        CharacterRecordId recordId,
        string displayName) =>
        GameplaySessionIdentityReadModelSnapshot.Create(
            [CreateContext(contextId, accountStableId, recordId, displayName)],
            DateTimeOffset.UtcNow,
            1);

    private static LiveMonitoringContextIdentityReadModel CreateContext(
        MonitoringContextId contextId,
        string accountStableId,
        CharacterRecordId recordId,
        string displayName) =>
        new()
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            AccountStableId = accountStableId,
            AccountDisplayName = accountStableId,
            CharacterRecordId = recordId,
            CharacterDisplayName = displayName,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            SessionStartedAt = DateTimeOffset.UtcNow,
            NeedsAttention = false,
            CandidateCount = 0,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: " + displayName
        };

    private sealed class FakeGameplaySessionIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

        public void RaiseChanged() =>
            Changed?.Invoke(
                this,
                new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = Current });
    }
}
