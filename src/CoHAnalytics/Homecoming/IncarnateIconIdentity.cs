namespace CoHAnalytics.Homecoming;

/// <summary>Canonical Homecoming client identities for Incarnate icon assets.</summary>
public static class IncarnateIconIdentity
{
    /// <summary>The only dedicated blank Incarnate slot artwork present in the client.</summary>
    public const string AlphaBlank = "Incarnate_Alpha_Blank.tga";

    /// <summary>
    /// Creates a client icon identity from the raw slot, branch, and tier filename tokens.
    /// Returns <see langword="null"/> when any token is absent or is not a single path-safe token.
    /// </summary>
    public static string? TryCreateBranchTier(
        string? slotToken,
        string? branchToken,
        string? tierToken)
    {
        if (!IsAssetToken(slotToken) || !IsAssetToken(branchToken) || !IsAssetToken(tierToken))
        {
            return null;
        }

        return $"Incarnate_{slotToken!.Trim()}_{branchToken!.Trim()}_{tierToken!.Trim()}.tga";
    }

    private static bool IsAssetToken(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Any(char.IsWhiteSpace)
        && value.IndexOfAny(['/', '\\', '.']) < 0;
}
