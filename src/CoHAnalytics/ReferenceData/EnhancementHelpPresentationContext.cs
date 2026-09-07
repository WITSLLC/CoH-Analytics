namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Explicit static presentation level for Enhancement help Scale resolution.
/// Represents player/security level only — not combat/exemplar level.
/// </summary>
public sealed record EnhancementHelpPresentationContext
{
    public required int PresentationLevel { get; init; }
}
