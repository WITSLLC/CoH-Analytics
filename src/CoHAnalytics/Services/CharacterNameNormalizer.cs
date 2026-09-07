using System.Text;

namespace CoHAnalytics.Services;

/// <summary>
/// Normalizes character display names for exact account-scoped repository matching (Revision 9
/// §3.6.22.5). Display names retain original casing; normalization is used only for lookup keys.
/// </summary>
internal static class CharacterNameNormalizer
{
  public static string Normalize(string displayName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

    return displayName.Trim().Normalize(NormalizationForm.FormC);
  }

  public static bool NamesMatch(string normalizedLeft, string normalizedRight) =>
      string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);

  public static bool IsValidDisplayName(string? displayName) =>
      !string.IsNullOrWhiteSpace(displayName);
}
