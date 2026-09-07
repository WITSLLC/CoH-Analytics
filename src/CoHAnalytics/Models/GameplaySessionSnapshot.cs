namespace CoHAnalytics.Models;

/// <summary>Immutable observation of one gameplay session at a point in time.</summary>
public sealed record GameplaySessionSnapshot
{
    public required GameplaySessionId SessionId { get; init; }

    public required MonitoringContextId ContextId { get; init; }

    public string? AccountStableId { get; init; }

    public required GameplaySessionLifecycleState LifecycleState { get; init; }

    public CharacterRecordId? CharacterRecordId { get; init; }

    public string? CharacterDisplayName { get; init; }

    public required CharacterIdentityConfidence CharacterIdentityConfidence { get; init; }

    public required CharacterIdentityResolutionState CharacterIdentityResolutionState { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? SuspendedAt { get; init; }

    public DateTimeOffset? FinalizedAt { get; init; }

    public long CurrentSourceBindingGeneration { get; init; }

    public MonitoringSourceTransitionKind CurrentSourceTransitionKind { get; init; }

    public int RetainedEventCount { get; init; }

    public long RetainedPayloadBytes { get; init; }

    public long DiscardedAfterOverflowCount { get; init; }

    public bool RetentionOverflowed { get; init; }

    public bool NeedsAttention { get; init; }

    public int CandidateCount { get; init; }

    public IReadOnlyList<CharacterIdentityCandidate> IdentityCandidates { get; init; } = [];

    public DateTimeOffset? LastEventAt { get; init; }

    public long SessionExperienceGained { get; init; }

    public long SessionGameplayInfluenceGained { get; init; }

    public IReadOnlyList<GameplaySessionRecentRewardEntry> RecentRewards { get; init; } = [];

    public IReadOnlyList<GameplaySessionRewardCurrencyTotal> RewardCurrencyTotals { get; init; } = [];

    public IReadOnlyList<GameplaySessionItemTotal> SalvageTotals { get; init; } = [];

    public IReadOnlyList<GameplaySessionItemTotal> EnhancementTotals { get; init; } = [];

    public IReadOnlyList<GameplaySessionItemTotal> RecipeTotals { get; init; } = [];

    public IReadOnlyList<GameplaySessionItemTotal> InspirationTotals { get; init; } = [];

    public GameplaySessionRewardCategoryCounts RewardCategoryCounts { get; init; } =
        new GameplaySessionRewardCategoryCounts();

    public int RetainedCombatEventCount { get; init; }

    public CombatSnapshot Combat { get; init; } = CombatSnapshot.Empty;

    public RollingEarningsScopeSnapshot RollingEarnings { get; init; } = RollingEarningsScopeSnapshot.Empty;

    public TrackedEarningsScopeSnapshot TrackedEarnings { get; init; } = TrackedEarningsScopeSnapshot.Empty;
}
