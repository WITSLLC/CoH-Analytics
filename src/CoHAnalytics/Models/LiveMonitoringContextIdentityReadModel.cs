namespace CoHAnalytics.Models;

/// <summary>
/// UI-facing runtime identity state for one monitoring context.
/// </summary>
public sealed record LiveMonitoringContextIdentityReadModel
{
    public required MonitoringContextId ContextId { get; init; }

    public required MonitoringContextState ContextState { get; init; }

    public string? AccountStableId { get; init; }

    public string? AccountDisplayName { get; init; }

  /// <summary>
  /// Durable character identity anchor for the active session when resolved.
  /// Implements the documented <c>CharacterStableId</c> concept.
  /// </summary>
    public CharacterRecordId? CharacterRecordId { get; init; }

    public string? CharacterDisplayName { get; init; }

    public required CharacterIdentityConfidence CharacterIdentityConfidence { get; init; }

    public required CharacterIdentityResolutionState CharacterIdentityResolutionState { get; init; }

    public GameplaySessionLifecycleState? SessionLifecycleState { get; init; }

    public bool HasActiveSession { get; init; }

    public bool NeedsAttention { get; init; }

    public int CandidateCount { get; init; }

    public IReadOnlyList<CharacterIdentityCandidate> IdentityCandidates { get; init; } = [];

    public IReadOnlyList<CharacterPickerOptionReadModel> PickerCharacters { get; init; } = [];

    public bool RequiresManualSelection { get; init; }

    public required string IdentityStatusLabel { get; init; }

    public required string IdentityDetail { get; init; }

    public bool IsConfirmed =>
        CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
        && CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed;

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

    public DateTimeOffset? SessionStartedAt { get; init; }

  /// <summary>
  /// When set, elapsed session duration is measured to this instant instead of the live clock
  /// (finalized or suspended sessions).
  /// </summary>
    public DateTimeOffset? SessionTimingEndAt { get; init; }

    public int RetainedCombatEventCount { get; init; }

    public CombatSnapshot Combat { get; init; } = CombatSnapshot.Empty;

    public RollingEarningsScopeSnapshot RollingEarnings { get; init; } = RollingEarningsScopeSnapshot.Empty;

    public TrackedEarningsScopeSnapshot TrackedEarnings { get; init; } = TrackedEarningsScopeSnapshot.Empty;
}
