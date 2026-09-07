namespace CoHAnalytics.Models;

/// <summary>
/// One historical display name retained when a character record's current display name changes.
/// </summary>
public sealed record CharacterAlias
{
  public required string DisplayName { get; init; }

  public required DateTimeOffset FirstObservedAt { get; init; }

  public required DateTimeOffset LastObservedAt { get; init; }
}
