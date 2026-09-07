using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Builds Live Session identity read models from monitoring, gameplay-session, and character-repository state.
/// </summary>
public sealed class GameplaySessionIdentityReadService : IGameplaySessionIdentityReadService, IDisposable
{
    private readonly IGameplaySessionManager _gameplaySessionManager;
    private readonly IMonitoringSessionManager _monitoringSessionManager;
    private readonly ICharacterRepository _characterRepository;
    private readonly object _sync = new();
    private GameplaySessionIdentityReadModelSnapshot _current = GameplaySessionIdentityReadModelSnapshot.Empty;
    private long _revision;
    private bool _disposed;

    public GameplaySessionIdentityReadService(
        IGameplaySessionManager gameplaySessionManager,
        IMonitoringSessionManager monitoringSessionManager,
        ICharacterRepository characterRepository)
    {
        _gameplaySessionManager = gameplaySessionManager;
        _monitoringSessionManager = monitoringSessionManager;
        _characterRepository = characterRepository;

        _gameplaySessionManager.StateChanged += OnSourceChanged;
        _monitoringSessionManager.StateChanged += OnSourceChanged;
        _characterRepository.StateChanged += OnSourceChanged;
        RebuildSnapshot();
    }

    public GameplaySessionIdentityReadModelSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gameplaySessionManager.StateChanged -= OnSourceChanged;
        _monitoringSessionManager.StateChanged -= OnSourceChanged;
        _characterRepository.StateChanged -= OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, EventArgs e) => RebuildSnapshot();

    private void RebuildSnapshot()
    {
        if (_disposed)
        {
            return;
        }

        var observedAt = DateTimeOffset.UtcNow;
        var contexts = BuildContexts();
        var snapshot = GameplaySessionIdentityReadModelSnapshot.Create(contexts, observedAt, ++_revision);

        lock (_sync)
        {
            _current = snapshot;
        }

        Changed?.Invoke(this, new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = snapshot });
    }

    private List<LiveMonitoringContextIdentityReadModel> BuildContexts()
    {
        var monitoring = _monitoringSessionManager.Current;
        var gameplay = _gameplaySessionManager.Current;
        var characters = _characterRepository.Current.Records;

        return monitoring.Contexts
            .Where(context => context.State != MonitoringContextState.Stopped)
            .Select(context =>
            {
                var session = SelectPresentationSession(gameplay.Sessions, context.ContextId);
                var activeSession = gameplay.Sessions.FirstOrDefault(item =>
                    item.ContextId == context.ContextId
                    && item.LifecycleState == GameplaySessionLifecycleState.Active);

                var pickerCharacters = BuildPickerCharacters(context.AccountStableId, characters);
                var presentation = MapIdentityPresentation(session, context.State, activeSession is not null);

                return new LiveMonitoringContextIdentityReadModel
                {
                    ContextId = context.ContextId,
                    ContextState = context.State,
                    AccountStableId = context.AccountStableId,
                    AccountDisplayName = context.AccountDisplayName,
                    CharacterRecordId = session?.CharacterRecordId,
                    CharacterDisplayName = session?.CharacterDisplayName,
                    CharacterIdentityConfidence = session?.CharacterIdentityConfidence ?? CharacterIdentityConfidence.Unknown,
                    CharacterIdentityResolutionState = session?.CharacterIdentityResolutionState
                        ?? CharacterIdentityResolutionState.Unresolved,
                    SessionLifecycleState = session?.LifecycleState,
                    HasActiveSession = activeSession is not null,
                    NeedsAttention = session?.NeedsAttention ?? false,
                    CandidateCount = session?.CandidateCount ?? 0,
                    IdentityCandidates = session?.IdentityCandidates ?? [],
                    PickerCharacters = pickerCharacters,
                    RequiresManualSelection = presentation.RequiresManualSelection && pickerCharacters.Count > 0,
                    IdentityStatusLabel = presentation.StatusLabel,
                    IdentityDetail = presentation.Detail,
                    SessionExperienceGained = session?.SessionExperienceGained ?? 0,
                    SessionGameplayInfluenceGained = session?.SessionGameplayInfluenceGained ?? 0,
                    RecentRewards = session?.RecentRewards ?? [],
                    RewardCurrencyTotals = session?.RewardCurrencyTotals ?? [],
                    SalvageTotals = session?.SalvageTotals ?? [],
                    EnhancementTotals = session?.EnhancementTotals ?? [],
                    RecipeTotals = session?.RecipeTotals ?? [],
                    InspirationTotals = session?.InspirationTotals ?? [],
                    RewardCategoryCounts = session?.RewardCategoryCounts ?? new GameplaySessionRewardCategoryCounts(),
                    SessionStartedAt = session?.StartedAt,
                    SessionTimingEndAt = ResolveSessionTimingEndAt(session),
                    RetainedCombatEventCount = session?.RetainedCombatEventCount ?? 0,
                    Combat = session?.Combat ?? CombatSnapshot.Empty,
                    RollingEarnings = session?.RollingEarnings ?? RollingEarningsScopeSnapshot.Empty,
                    TrackedEarnings = session?.TrackedEarnings ?? TrackedEarningsScopeSnapshot.Empty
                };
            })
            .ToList();
    }

    private static DateTimeOffset? ResolveSessionTimingEndAt(GameplaySessionSnapshot? session)
    {
        if (session is null)
        {
            return null;
        }

        if (session.LifecycleState == GameplaySessionLifecycleState.Finalized)
        {
            return session.FinalizedAt ?? session.StartedAt;
        }

        if (session.LifecycleState == GameplaySessionLifecycleState.Suspended)
        {
            return session.SuspendedAt ?? session.StartedAt;
        }

        return null;
    }

    private static List<CharacterPickerOptionReadModel> BuildPickerCharacters(
        string? accountStableId,
        IReadOnlyList<CharacterRecord> records)
    {
        if (string.IsNullOrWhiteSpace(accountStableId))
        {
            return [];
        }

        return records
            .Where(record => string.Equals(record.AccountStableId, accountStableId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.LastObservedAt)
            .ThenBy(record => record.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(record => new CharacterPickerOptionReadModel
            {
                RecordId = record.RecordId,
                DisplayName = record.CurrentDisplayName,
                LastObservedAt = record.LastObservedAt
            })
            .ToList();
    }

    private static GameplaySessionSnapshot? SelectPresentationSession(
        IReadOnlyList<GameplaySessionSnapshot> sessions,
        MonitoringContextId contextId)
    {
        var forContext = sessions
            .Where(item => item.ContextId == contextId)
            .ToArray();

        return forContext.FirstOrDefault(item => item.LifecycleState == GameplaySessionLifecycleState.Active)
            ?? forContext.FirstOrDefault(item => item.LifecycleState == GameplaySessionLifecycleState.Suspended)
            ?? forContext
                .Where(item => item.LifecycleState == GameplaySessionLifecycleState.Finalized)
                .OrderByDescending(item => item.FinalizedAt ?? item.StartedAt)
                .FirstOrDefault();
    }

    private static IdentityPresentation MapIdentityPresentation(
        GameplaySessionSnapshot? session,
        MonitoringContextState contextState,
        bool hasActiveSession)
    {
        if (session is null)
        {
            return new IdentityPresentation(
                "No Active Session",
                "Gameplay identity appears when a monitored session is active for this context.",
                false);
        }

        var manualSelectionAllowed = hasActiveSession
            && contextState != MonitoringContextState.RuntimeSuspended
            && session.LifecycleState == GameplaySessionLifecycleState.Active;

        if (session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
            && session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed)
        {
            var name = string.IsNullOrWhiteSpace(session.CharacterDisplayName)
                ? "Unknown"
                : session.CharacterDisplayName;
            var statusLabel = session.LifecycleState == GameplaySessionLifecycleState.Active
                ? "Confirmed"
                : "Ended";
            return new IdentityPresentation(
                statusLabel,
                session.LifecycleState == GameplaySessionLifecycleState.Active
                    ? $"Active character: {name}"
                    : $"Last character: {name}",
                false);
        }

        if (session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.IdentityRequired)
        {
            return new IdentityPresentation(
                "Identity Required",
                "Gameplay activity is no longer being saved for this session. Select the active character to resume.",
                manualSelectionAllowed);
        }

        if (session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Conflicted)
        {
            return new IdentityPresentation(
                "Conflicted",
                "Character identity evidence could not be reconciled to one account-scoped character.",
                manualSelectionAllowed);
        }

        if (session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Candidate)
        {
            return new IdentityPresentation(
                "Unknown",
                "Identity candidates were observed. Select a character or wait for a Welcome message.",
                manualSelectionAllowed);
        }

        return new IdentityPresentation(
            "Unknown",
            "Character identity has not been established yet. Gameplay continues and events are retained while possible.",
            manualSelectionAllowed);
    }

    private sealed record IdentityPresentation(string StatusLabel, string Detail, bool RequiresManualSelection);
}
