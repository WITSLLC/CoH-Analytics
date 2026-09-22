using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

internal enum LocalCandidateHalfKind
{
    IncomingHeal,
    OutgoingHeal,
    IncomingHit,
    OutgoingHit,
    IncomingAutohit,
    OutgoingAutohit
}

internal readonly record struct LocalCandidateHalf(
    LocalCandidateHalfKind Kind,
    string DisplayName,
    string NormalizedName,
    string NormalizedPower,
    DateTimeOffset PairingAt);

/// <summary>
/// Conservative first-use identity halves. A candidate is never implied by one side; the
/// gameplay-session manager pairs opposite halves that agree on name, power, and time.
/// </summary>
internal static partial class LocalCharacterCandidateEvidence
{
    public static bool TryParseHalf(ParserEvent parserEvent, out LocalCandidateHalf half)
    {
        half = default;
        if (parserEvent.EventKind is ParserEventKind.ChatLine or ParserEventKind.Malformed)
        {
            return false;
        }

        if (CharacterIdentityResolver.IsWelcomeEvidence(parserEvent))
        {
            return false;
        }

        if (!ParserLineEnvelope.TryGetBody(parserEvent.RawLine, parserEvent.SourceId.LogDate, out var body)
            || string.IsNullOrEmpty(body)
            || body.StartsWith('[')
            || OuterPetPrefix().IsMatch(body))
        {
            return false;
        }

        if (!TryParseBody(body, out var kind, out var displayName, out var power))
        {
            return false;
        }

        half = new LocalCandidateHalf(
            kind,
            displayName,
            CharacterIdentityResolver.NormalizeName(displayName),
            NormalizePower(power),
            ResolvePairingAt(parserEvent));
        return true;
    }

    public static bool AreOppositeHalves(LocalCandidateHalfKind left, LocalCandidateHalfKind right) =>
        (left, right) switch
        {
            (LocalCandidateHalfKind.IncomingHeal, LocalCandidateHalfKind.OutgoingHeal) => true,
            (LocalCandidateHalfKind.OutgoingHeal, LocalCandidateHalfKind.IncomingHeal) => true,
            (LocalCandidateHalfKind.IncomingHit, LocalCandidateHalfKind.OutgoingHit) => true,
            (LocalCandidateHalfKind.OutgoingHit, LocalCandidateHalfKind.IncomingHit) => true,
            (LocalCandidateHalfKind.IncomingAutohit, LocalCandidateHalfKind.OutgoingAutohit) => true,
            (LocalCandidateHalfKind.OutgoingAutohit, LocalCandidateHalfKind.IncomingAutohit) => true,
            _ => false
        };

    public static bool HalvesMatch(LocalCandidateHalf left, LocalCandidateHalf right, TimeSpan window) =>
        AreOppositeHalves(left.Kind, right.Kind)
        && CharacterNameNormalizer.NamesMatch(left.NormalizedName, right.NormalizedName)
        && string.Equals(left.NormalizedPower, right.NormalizedPower, StringComparison.Ordinal)
        && AbsDelta(left.PairingAt, right.PairingAt) <= window;

    private static bool TryParseBody(
        string body,
        out LocalCandidateHalfKind kind,
        out string displayName,
        out string power)
    {
        if (TryMatch(OutgoingAutohit(), body, "name", "power", out displayName, out power))
        {
            kind = LocalCandidateHalfKind.OutgoingAutohit;
            return true;
        }

        if (TryMatch(IncomingAutohit(), body, "name", "power", out displayName, out power))
        {
            kind = LocalCandidateHalfKind.IncomingAutohit;
            return true;
        }

        if (TryMatch(IncomingHeal(), body, "name", "power", out displayName, out power))
        {
            kind = LocalCandidateHalfKind.IncomingHeal;
            return true;
        }

        if (TryMatch(OutgoingHeal(), body, "name", "power", out displayName, out power))
        {
            kind = LocalCandidateHalfKind.OutgoingHeal;
            return true;
        }

        if (TryMatch(IncomingHitGrant(), body, "name", "power", out displayName, out power)
            || TryMatch(IncomingHitDamage(), body, "name", "power", out displayName, out power))
        {
            kind = LocalCandidateHalfKind.IncomingHit;
            return true;
        }

        if (TryMatch(OutgoingHitGrant(), body, "name", "power", out displayName, out power)
            || TryMatch(OutgoingHitDamage(), body, "name", "power", out displayName, out power))
        {
            kind = LocalCandidateHalfKind.OutgoingHit;
            return true;
        }

        kind = default;
        displayName = string.Empty;
        power = string.Empty;
        return false;
    }

    private static bool TryMatch(
        Regex regex,
        string body,
        string nameGroup,
        string powerGroup,
        out string displayName,
        out string power)
    {
        displayName = string.Empty;
        power = string.Empty;
        var match = regex.Match(body);
        if (!match.Success)
        {
            return false;
        }

        displayName = match.Groups[nameGroup].Value.Trim();
        power = match.Groups[powerGroup].Value.Trim();
        return IsPlausibleCharacterName(displayName) && power.Length > 0;
    }

    private static bool IsPlausibleCharacterName(string displayName)
    {
        if (!CharacterNameNormalizer.IsValidDisplayName(displayName))
        {
            return false;
        }

        if (displayName.Contains(':')
            || displayName.Contains('[')
            || displayName.Contains(" with ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(displayName, "you", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(displayName, "yourself", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePower(string power) =>
        power.Trim().Normalize().ToLowerInvariant();

    private static DateTimeOffset ResolvePairingAt(ParserEvent parserEvent) =>
        parserEvent.SourceTimestamp is { } sourceTimestamp
            ? new DateTimeOffset(sourceTimestamp, TimeSpan.Zero)
            : parserEvent.ObservedAt;

    private static TimeSpan AbsDelta(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left - right : right - left;

    [GeneratedRegex(
        @"^(?<name>.+?) heals you with their (?<power>.+?) for \d",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex IncomingHeal();

    [GeneratedRegex(
        @"^You heal (?<name>.+?) with (?!their )(?<power>.+?) for \d",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex OutgoingHeal();

    [GeneratedRegex(
        @"^(?<name>.+?) hits you with their (?<power>.+?) granting you \d",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex IncomingHitGrant();

    [GeneratedRegex(
        @"^You hit (?<name>.+?) with your (?<power>.+?) granting them \d",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex OutgoingHitGrant();

    [GeneratedRegex(
        @"^(?<name>.+?) hits you with their (?<power>.+?) for \d",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex IncomingHitDamage();

    [GeneratedRegex(
        @"^You hit (?<name>.+?) with your (?<power>.+?) for \d",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex OutgoingHitDamage();

    [GeneratedRegex(
        @"^HIT (?<name>.+?)! Your (?<power>.+?) power is autohit\.$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex OutgoingAutohit();

    [GeneratedRegex(
        @"^(?<name>.+?) HITS you! (?<power>.+?) power was autohit\.$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex IncomingAutohit();

    [GeneratedRegex(@"^[^:\r\n]{1,80}:  ", RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex OuterPetPrefix();
}
