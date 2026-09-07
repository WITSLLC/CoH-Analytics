namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Resolves supported Homecoming Enhancement Scale help tokens from I1 canonical facts
/// without runtime PIGG reads.
/// </summary>
public interface IEnhancementHelpResolver
{
    /// <summary>
    /// Replaces supported <c>{Boost.Attrib.&lt;Tag&gt;.Scale}</c> tokens in <paramref name="template"/>
    /// using the supplied source variant facts, NamedTables, and presentation level.
    /// </summary>
    EnhancementHelpResolutionResult Resolve(
        string template,
        EnhancementSourceVariantReferenceRecord variant,
        EnhancementHelpPresentationContext context);
}
