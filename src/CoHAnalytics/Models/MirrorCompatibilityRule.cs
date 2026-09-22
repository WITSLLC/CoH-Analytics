namespace CoHAnalytics.Models;

/// <summary>Independent evidence a mirror rule requires in addition to matching semantics.</summary>
public enum MirrorDiscriminatorKind
{
    ActualSourceChannel = 1
}

/// <summary>
/// One enabled allowlist entry. Same-family pairs are rejected at construction.
/// Sequence/offset/timestamp fields are candidate filters, not independent proof.
/// </summary>
public sealed record MirrorCompatibilityRule
{
    public required string RuleId { get; init; }

    public required CombatEventFamily LeftFamily { get; init; }

    public required CombatEventFamily RightFamily { get; init; }

    public required MirrorDiscriminatorKind RequiredDiscriminator { get; init; }

    /// <summary>Actual log channel required on the left-family representation.</summary>
    public required string LeftChannel { get; init; }

    /// <summary>Actual log channel required on the right-family representation.</summary>
    public required string RightChannel { get; init; }

    public int MaxSequenceDistance { get; init; } = 2;

    public EventFacets FacetUnion { get; init; }

    public bool MatchesFamilies(CombatEventFamily first, CombatEventFamily second) =>
        (first == LeftFamily && second == RightFamily)
        || (first == RightFamily && second == LeftFamily);

    public bool ChannelsAreComplementary(
        CombatEventFamily firstFamily, string? firstChannel,
        CombatEventFamily secondFamily, string? secondChannel)
    {
        if (!MatchesFamilies(firstFamily, secondFamily)
            || string.IsNullOrWhiteSpace(firstChannel) || string.IsNullOrWhiteSpace(secondChannel))
        {
            return false;
        }

        if (string.Equals(firstChannel, secondChannel, StringComparison.Ordinal))
        {
            return false;
        }

        return firstFamily == LeftFamily
            ? ChannelsMatchPair(firstChannel, secondChannel, LeftChannel, RightChannel)
            : ChannelsMatchPair(firstChannel, secondChannel, RightChannel, LeftChannel);
    }

    private static bool ChannelsMatchPair(
        string firstChannel,
        string secondChannel,
        string expectedFirst,
        string expectedSecond) =>
        string.Equals(firstChannel, expectedFirst, StringComparison.Ordinal)
        && string.Equals(secondChannel, expectedSecond, StringComparison.Ordinal);
}
