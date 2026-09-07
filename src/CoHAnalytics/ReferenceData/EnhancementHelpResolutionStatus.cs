namespace CoHAnalytics.ReferenceData;

/// <summary>Outcome of static Enhancement help Scale token resolution.</summary>
public enum EnhancementHelpResolutionStatus
{
    /// <summary>All supported Scale tokens in the template were resolved.</summary>
    FullyResolved,

    /// <summary>Some supported Scale tokens resolved; others remain unchanged.</summary>
    PartiallyResolved,

    /// <summary>The template contained no supported Scale tokens.</summary>
    Unchanged,

    /// <summary>Input could not be resolved (for example invalid presentation level).</summary>
    InvalidInput,
}
