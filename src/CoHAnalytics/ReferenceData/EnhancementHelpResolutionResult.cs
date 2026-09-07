namespace CoHAnalytics.ReferenceData;

/// <summary>Player-facing help text produced by static Scale token resolution.</summary>
public sealed record EnhancementHelpResolutionResult
{
    public required EnhancementHelpResolutionStatus Status { get; init; }

    /// <summary>Template with resolved Scale numeric inserts. Unresolved tokens remain verbatim.</summary>
    public required string ResolvedText { get; init; }

    /// <summary>Supported Scale token spellings that could not be resolved.</summary>
    public IReadOnlyList<string> UnresolvedTokens { get; init; } = Array.Empty<string>();

    /// <summary>Human-readable diagnostics for partial or invalid outcomes.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}
