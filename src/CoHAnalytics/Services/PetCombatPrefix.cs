using System.Text.RegularExpressions;

namespace CoHAnalytics.Services;

/// <summary>
/// Verified own-pet combat prefix: <c>Entity:  </c> (colon + two spaces), never channel/speaker chat.
/// A colon prefix alone is not ownership; callers must rematch the inner combat grammar.
/// </summary>
internal static partial class PetCombatPrefix
{
    public static bool TryStrip(string body, out string entity, out string inner)
    {
        entity = string.Empty;
        inner = string.Empty;
        if (string.IsNullOrEmpty(body) || body.StartsWith('['))
        {
            return false;
        }

        var match = Pattern().Match(body);
        if (!match.Success)
        {
            return false;
        }

        entity = match.Groups["entity"].Value;
        inner = match.Groups["inner"].Value;
        return !string.IsNullOrWhiteSpace(entity) && inner.Length > 0;
    }

    [GeneratedRegex(@"^(?<entity>[^:\[\]\r\n]{1,40}):  (?<inner>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
