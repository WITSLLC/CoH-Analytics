namespace CoHAnalytics.Services;

/// <summary>Requests explicit user confirmation before selected historical segments are deleted.</summary>
public interface IHistoricalSegmentDeleteConfirmationService
{
    bool ConfirmDelete(HistoricalSegmentDeleteConfirmationRequest request);
}

public sealed record HistoricalSegmentDeleteConfirmationRequest
{
    public required int SegmentCount { get; init; }

    public required string CharacterLabel { get; init; }

    public required string AccountLabel { get; init; }

    public required string DateTimeLabel { get; init; }

    public string Headline => SegmentCount == 1
        ? "Delete 1 selected segment?"
        : $"Delete {SegmentCount} selected segments?";

    public bool ShowSegmentIdentity => SegmentCount == 1;
}
