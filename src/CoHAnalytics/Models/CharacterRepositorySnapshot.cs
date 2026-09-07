namespace CoHAnalytics.Models;

/// <summary>
/// An immutable aggregate view of every trusted character record held by the repository.
/// </summary>
public sealed record CharacterRepositorySnapshot
{
  private CharacterRepositorySnapshot(
      IReadOnlyList<CharacterRecord> records,
      int skippedCorruptRecordCount,
      DateTimeOffset observedAt,
      long revision)
  {
    Records = records;
    SkippedCorruptRecordCount = skippedCorruptRecordCount;
    ObservedAt = observedAt;
    Revision = revision;
  }

  public IReadOnlyList<CharacterRecord> Records { get; }

  /// <summary>
  /// Records rejected during the most recent load because they failed validation. Does not
  /// change on ordinary mutations unless a reload occurs.
  /// </summary>
  public int SkippedCorruptRecordCount { get; init; }

  public DateTimeOffset ObservedAt { get; }

  public long Revision { get; }

  public int RecordCount => Records.Count;

  public static CharacterRepositorySnapshot Empty { get; } = new([], 0, default, 0);

  public static CharacterRepositorySnapshot Create(
      IEnumerable<CharacterRecord> records,
      int skippedCorruptRecordCount,
      DateTimeOffset observedAt,
      long revision) =>
      new(
          [.. records
              .OrderBy(record => record.AccountStableId, StringComparer.Ordinal)
              .ThenBy(record => record.NormalizedCharacterName, StringComparer.Ordinal)
              .ThenBy(record => record.RecordId.ToString(), StringComparer.Ordinal)],
          skippedCorruptRecordCount,
          observedAt,
          revision);

  public bool IsSemanticallyEquivalentTo(CharacterRepositorySnapshot other)
  {
    if (SkippedCorruptRecordCount != other.SkippedCorruptRecordCount
        || Records.Count != other.Records.Count)
    {
      return false;
    }

    for (var index = 0; index < Records.Count; index++)
    {
      if (!Records[index].Equals(other.Records[index]))
      {
        return false;
      }
    }

    return true;
  }

  public CharacterRepositorySnapshot WithObservation(DateTimeOffset observedAt, long revision) =>
      new(Records, SkippedCorruptRecordCount, observedAt, revision);
}
