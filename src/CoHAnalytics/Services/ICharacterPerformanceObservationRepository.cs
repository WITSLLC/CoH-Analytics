using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Durable store for immutable per-character performance observations.</summary>
public interface ICharacterPerformanceObservationRepository
{
    string ObservationDirectory { get; }

    IReadOnlyList<string> MalformedFileReports { get; }

    event EventHandler? Changed;

    CharacterPerformanceObservationWriteResult Persist(CharacterPerformanceObservation observation);

    CharacterPerformanceObservationUpdateResult SetIncludeInOverview(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        bool includeInOverview);

    CharacterPerformanceObservationDeleteResult Delete(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal);

    IReadOnlyList<CharacterPerformanceObservation> GetByCharacter(CharacterRecordId characterRecordId);

    IReadOnlyList<CharacterPerformanceObservation> GetAll();
}

public enum CharacterPerformanceObservationDeleteOutcome
{
    Deleted,
    NotFound,
    PersistenceFailed
}

public sealed record CharacterPerformanceObservationDeleteResult
{
    public required CharacterPerformanceObservationDeleteOutcome Outcome { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess =>
        Outcome is CharacterPerformanceObservationDeleteOutcome.Deleted
            or CharacterPerformanceObservationDeleteOutcome.NotFound;
}

public enum CharacterPerformanceObservationUpdateOutcome
{
    Updated,
    Unchanged,
    NotFound,
    PersistenceFailed
}

public sealed record CharacterPerformanceObservationUpdateResult
{
    public required CharacterPerformanceObservationUpdateOutcome Outcome { get; init; }

    public string? FilePath { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess =>
        Outcome is CharacterPerformanceObservationUpdateOutcome.Updated
            or CharacterPerformanceObservationUpdateOutcome.Unchanged;
}

public enum CharacterPerformanceObservationWriteOutcome
{
    Persisted,
    Duplicate,
    Conflict,
    InvalidObservation,
    PersistenceFailed
}

public sealed record CharacterPerformanceObservationWriteResult
{
    public required CharacterPerformanceObservationWriteOutcome Outcome { get; init; }

    public string? FilePath { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess =>
        Outcome is CharacterPerformanceObservationWriteOutcome.Persisted
            or CharacterPerformanceObservationWriteOutcome.Duplicate;
}
