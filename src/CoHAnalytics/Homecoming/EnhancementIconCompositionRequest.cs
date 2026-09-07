using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Homecoming;

/// <summary>
/// Minimum proven inputs for composing a complete Homecoming Enhancement icon.
/// </summary>
public sealed record EnhancementIconCompositionRequest
{
    public required string IconIdentity { get; init; }

    /// <summary>
    /// Canonical Homecoming non-origin BOOST_TYPE key (for example <c>Damage</c>, <c>Accuracy</c>).
    /// </summary>
    public required string PogBoostType { get; init; }

    public required EnhancementFrameClass FrameClass { get; init; }

    public static EnhancementIconCompositionRequest? TryCreate(
        string? iconIdentity,
        string? pogBoostType,
        ReferenceEnhancementFamily? enhancementFamily,
        string? sourceForm,
        string? variant,
        string? setRarityCode)
    {
        if (string.IsNullOrWhiteSpace(iconIdentity) || string.IsNullOrWhiteSpace(pogBoostType))
        {
            return null;
        }

        var frameClass = EnhancementFrameClassResolver.TryResolve(
            enhancementFamily,
            sourceForm,
            variant,
            setRarityCode);
        if (frameClass is null)
        {
            return null;
        }

        return new EnhancementIconCompositionRequest
        {
            IconIdentity = iconIdentity.Trim(),
            PogBoostType = pogBoostType.Trim(),
            FrameClass = frameClass.Value
        };
    }
}
