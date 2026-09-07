namespace CoHAnalytics.Models;

/// <summary>
/// An immutable observation of one trusted character record at a point in time.
/// </summary>
public sealed record CharacterRecord
{
  public const int CurrentSchemaVersion = 2;

  public required CharacterRecordId RecordId { get; init; }

  public required string AccountStableId { get; init; }

  /// <summary>Stable user-facing identifier for buildsave import and slash commands.</summary>
  public string? CharacterShortId { get; init; }

  /// <summary>Normalized name used for exact account-scoped matching.</summary>
  public required string NormalizedCharacterName { get; init; }

  /// <summary>Current display name with original punctuation and casing.</summary>
  public required string CurrentDisplayName { get; init; }

  public required DateTimeOffset FirstObservedAt { get; init; }

  public required DateTimeOffset LastObservedAt { get; init; }

  public required CharacterTrustState TrustState { get; init; }

  public required CharacterProvenanceSummary Provenance { get; init; }

  public int? ObservedLevel { get; init; }

  public DateTimeOffset? ObservedLevelObservedAt { get; init; }

  public string? PrimaryPowerSet { get; init; }

  public string? SecondaryPowerSet { get; init; }

  public string? Archetype { get; init; }

  public int? CurrentBuildNumber { get; init; }

  public DateTimeOffset? BuildMetadataObservedAt { get; init; }

  public CharacterIconReference? IconReference { get; init; }

  public string ObservedLevelLabel => ObservedLevel is int level ? $"Level {level}" : "Level Unknown";

  /// <summary>Prior display names retained for future rename support.</summary>
  public IReadOnlyList<CharacterAlias> Aliases { get; init; } = [];

  public int RecordSchemaVersion { get; init; } = CurrentSchemaVersion;
}
