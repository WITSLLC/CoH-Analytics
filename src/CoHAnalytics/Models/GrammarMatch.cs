namespace CoHAnalytics.Models;

/// <summary>
/// One grammar-table hit. Captures are raw named groups; semantic conversion is owned by
/// <see cref="Services.Normalizer"/>.
/// </summary>
internal sealed record GrammarMatch
{
    public required CombatGrammarId GrammarId { get; init; }

    public required IReadOnlyDictionary<string, string> Captures { get; init; }

    /// <summary>Verified outer pet-prefix entity when the inner body was rematched in pet scope.</summary>
    public string? PrefixEntity { get; init; }

    public string Capture(string name) =>
        Captures.TryGetValue(name, out var value) ? value : string.Empty;
}
