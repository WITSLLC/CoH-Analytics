namespace CoHAnalytics.Services;

/// <summary>Requests explicit user confirmation before one historical segment is deleted.</summary>
public interface IHistoricalSegmentDeleteConfirmationService
{
    bool ConfirmDelete(HistoricalSegmentDeleteConfirmationRequest request);
}

public sealed record HistoricalSegmentDeleteConfirmationRequest
{
    public required string CharacterLabel { get; init; }

    public required string AccountLabel { get; init; }

    public required string DateTimeLabel { get; init; }
}
