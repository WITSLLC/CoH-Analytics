using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// In-memory developer-facing report describing the monitoring session manager's state.
/// </summary>
/// <remarks>Diagnostics never contain chat text or any parsed gameplay data or file paths.</remarks>
public sealed record MonitoringSessionManagerDiagnostics
{
    public required bool IsRunning { get; init; }

    public required bool IsRuntimeAvailable { get; init; }

    public required long LastSnapshotRevision { get; init; }

    public required int ContextCount { get; init; }

    public required IReadOnlyList<MonitoringContextSnapshot> Contexts { get; init; }

    public required IReadOnlyList<MonitoringSourceOffer> PendingOffers { get; init; }

    /// <summary>Source ids currently suppressed from re-offering because they were declined.</summary>
    public required IReadOnlyList<string> DeclineSuppressedSourceIds { get; init; }

    /// <summary>
    /// A bounded, most-recent-first log of claim/offer/rollover/removal decisions. Never
    /// contains chat text or file paths.
    /// </summary>
    public required IReadOnlyList<string> RecentDecisions { get; init; }

    public DateTimeOffset? LastReconciliationAt { get; init; }

    public TimeSpan? LastReconciliationDuration { get; init; }

    public string? LastReconciliationReason { get; init; }
}
