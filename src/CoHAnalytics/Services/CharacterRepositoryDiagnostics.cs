using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// In-memory developer-facing report describing the Character Repository state.
/// </summary>
/// <remarks>Diagnostics never contain raw chat text, parser payloads, or file paths.</remarks>
public sealed record CharacterRepositoryDiagnostics
{
  public required long LastSnapshotRevision { get; init; }

  public required int RecordCount { get; init; }

  public required int SkippedCorruptRecordCount { get; init; }

  public required int DiscardedAliasCount { get; init; }

  public required bool RepositoryFileCorrupted { get; init; }

  public required string PersistencePath { get; init; }

  public required int PersistenceSchemaVersion { get; init; }

  public DateTimeOffset? LastPersistedAt { get; init; }

  public DateTimeOffset? LastLoadAt { get; init; }

  public required bool HasUnsavedChanges { get; init; }

  public required IReadOnlyList<CharacterRecord> Records { get; init; }

  /// <summary>A bounded, most-recent-first log of repository operations. No raw chat text.</summary>
  public required IReadOnlyList<string> RecentOperations { get; init; }
}
