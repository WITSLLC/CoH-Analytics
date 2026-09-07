namespace CoHAnalytics.Replay;

public sealed class ReplaySegmentLedger
{
    public int Ordinal { get; init; }

    public long SourceBytes { get; set; }

    public long SourceCompleteLines { get; set; }

    public bool HasIncompleteFinalFragment { get; set; }

    public long IncompleteFinalFragmentBytes { get; set; }

    public long DestinationBytes { get; set; }

    public long DestinationCompleteLines { get; set; }

    public long PlannedChunks { get; set; }

    public long AppendedChunks { get; set; }
}

public sealed class ReplayLedger
{
  private readonly Dictionary<int, ReplaySegmentLedger> _segments = [];

  public long OriginalSourceBytes { get; private set; }

  public long OriginalSourceCompleteLines { get; private set; }

  public bool HasIncompleteFinalFragment { get; private set; }

  public long IncompleteFinalFragmentBytes { get; private set; }

  public long BootstrapBytes { get; private set; }

  public long BootstrapCompleteLines { get; private set; }

  public long CombinedDestinationBytes { get; private set; }

  public long CombinedDestinationCompleteLines { get; private set; }

  public long PlannedChunks { get; private set; }

  public long AppendedChunks { get; private set; }

  public long Flushes { get; private set; }

  public long BurstCount { get; private set; }

  public long RolloverCount { get; private set; }

  public bool BeginsMidSession { get; private set; }

  public ReplayPacingSummary Pacing { get; private set; } = new();

  public IReadOnlyDictionary<int, ReplaySegmentLedger> Segments => _segments;

  public ReplaySegmentLedger GetOrCreateSegment(int ordinal)
  {
    if (!_segments.TryGetValue(ordinal, out var segment))
    {
      segment = new ReplaySegmentLedger { Ordinal = ordinal };
      _segments[ordinal] = segment;
    }

    return segment;
  }

  public void RecordSourceAnalysis(int ordinal, long bytes, long completeLines, bool hasIncomplete, long incompleteBytes)
  {
    var segment = GetOrCreateSegment(ordinal);
    segment.SourceBytes = bytes;
    segment.SourceCompleteLines = completeLines;
    segment.HasIncompleteFinalFragment = hasIncomplete;
    segment.IncompleteFinalFragmentBytes = incompleteBytes;

    OriginalSourceBytes += bytes;
    OriginalSourceCompleteLines += completeLines;
    if (hasIncomplete)
    {
      HasIncompleteFinalFragment = true;
      IncompleteFinalFragmentBytes += incompleteBytes;
    }
  }

  public void RecordBootstrap(int ordinal, long bytes, long completeLines)
  {
    BootstrapBytes = bytes;
    BootstrapCompleteLines = completeLines;
    CombinedDestinationBytes += bytes;
    CombinedDestinationCompleteLines += completeLines;
    GetOrCreateSegment(ordinal).DestinationBytes += bytes;
    GetOrCreateSegment(ordinal).DestinationCompleteLines += completeLines;
  }

  public void RecordPlannedChunk(int ordinal)
  {
    GetOrCreateSegment(ordinal).PlannedChunks++;
    PlannedChunks++;
  }

  public void RecordAppendedChunk(int ordinal, int byteCount, int completeLinesAdded, bool endsWithIncompleteLine)
  {
    var segment = GetOrCreateSegment(ordinal);
    segment.AppendedChunks++;
    segment.DestinationBytes += byteCount;
    segment.DestinationCompleteLines += completeLinesAdded;
    AppendedChunks++;
    CombinedDestinationBytes += byteCount;
    CombinedDestinationCompleteLines += completeLinesAdded;

    if (endsWithIncompleteLine)
    {
      HasIncompleteFinalFragment = true;
    }
  }

  public void RecordFlush() => Flushes++;

  public void RecordBurst() => BurstCount++;

  public void RecordRollover() => RolloverCount++;

  public void MarkBeginsMidSession() => BeginsMidSession = true;

  public void SetPacing(ReplayPacingSummary pacing) => Pacing = pacing;

  public void Validate(ReplayPlan plan)
  {
    if (AppendedChunks != PlannedChunks)
    {
      throw new ReplayAccountingException(
          $"Appended chunks ({AppendedChunks}) do not match planned chunks ({PlannedChunks}).");
    }

    var expectedBootstrapBytes = plan.InputMode == ReplayInputMode.Bootstrap ? ReplayPlan.BootstrapBytes.Length : 0;
    if (BootstrapBytes != expectedBootstrapBytes)
    {
      throw new ReplayAccountingException(
          $"Bootstrap bytes ({BootstrapBytes}) do not match expected ({expectedBootstrapBytes}).");
    }

  foreach (var segment in _segments.Values)
    {
      if (segment.AppendedChunks != segment.PlannedChunks)
      {
        throw new ReplayAccountingException(
            $"Segment {segment.Ordinal} appended chunks ({segment.AppendedChunks}) do not match planned ({segment.PlannedChunks}).");
      }

      var expectedDestination = segment.SourceBytes
          + (plan.InputMode == ReplayInputMode.Bootstrap && segment.Ordinal == 1 ? BootstrapBytes : 0);
      if (segment.DestinationBytes != expectedDestination)
      {
        throw new ReplayAccountingException(
            $"Segment {segment.Ordinal} destination bytes ({segment.DestinationBytes}) do not match expected ({expectedDestination}).");
      }
    }

    var expectedCombined = OriginalSourceBytes + BootstrapBytes;
    if (CombinedDestinationBytes != expectedCombined)
    {
      throw new ReplayAccountingException(
          $"Combined destination bytes ({CombinedDestinationBytes}) do not match source plus bootstrap ({expectedCombined}).");
    }
  }
}

public sealed class ReplayAccountingException : Exception
{
  public ReplayAccountingException(string message) : base(message)
  {
  }
}

internal static class ReplayLineAccounting
{
  public static (long CompleteLines, bool HasIncompleteFinalFragment, long IncompleteBytes) Analyze(byte[] content)
  {
    if (content.Length == 0)
    {
      return (0, false, 0);
    }

    long completeLines = 0;
    var index = 0;
    while (index < content.Length)
    {
      var lineStart = index;
      while (index < content.Length && content[index] != (byte)'\n')
      {
        index++;
      }

      if (index < content.Length)
      {
        completeLines++;
        index++;
        continue;
      }

      var trailing = content.Length - lineStart;
      return trailing == 0
          ? (completeLines, false, 0)
          : (completeLines, true, trailing);
    }

    return (completeLines, false, 0);
  }

  public static (int CompleteLinesAdded, bool EndsWithIncompleteLine) CountLinesInAppend(
      ReadOnlySpan<byte> chunk,
      bool destinationEndsWithIncompleteLine)
  {
    var completeLinesAdded = 0;
    var incomplete = destinationEndsWithIncompleteLine;

    for (var index = 0; index < chunk.Length; index++)
    {
      if (chunk[index] == (byte)'\n')
      {
        completeLinesAdded++;
        incomplete = false;
      }
    }

    if (chunk.Length > 0 && chunk[^1] != (byte)'\n')
    {
      incomplete = true;
    }

    return (completeLinesAdded, incomplete);
  }
}
