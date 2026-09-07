using System.Security.Cryptography;

namespace CoHAnalytics.Models;

/// <summary>
/// Persistent user-facing short identifier for one character record. Stable for the lifetime of
/// the record and independent of display name or account path changes.
/// </summary>
public sealed record CharacterShortId
{
    public const int Length = 8;

    public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private CharacterShortId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static CharacterShortId CreateNew() => new(GenerateUniqueValue());

    public static CharacterShortId FromCanonical(string canonicalValue) => new(canonicalValue);

    public static bool TryParse(string? value, out CharacterShortId shortId)
    {
        if (!TryGetCanonical(value, out var canonical))
        {
            shortId = null!;
            return false;
        }

        shortId = new CharacterShortId(canonical);
        return true;
    }

    public static bool IsValid(string? value) => TryGetCanonical(value, out _);

    public static string FormatBuildSaveCommand(CharacterShortId shortId) =>
        $"/buildsavefile {shortId.Value}.txt";

    public static string GetBuildFileName(CharacterShortId shortId) => $"{shortId.Value}.txt";

    private static string GenerateUniqueValue()
    {
        Span<char> buffer = stackalloc char[Length];
        for (var index = 0; index < Length; index++)
        {
            buffer[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }

    private static bool TryGetCanonical(string? value, out string canonical)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            canonical = string.Empty;
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        if (trimmed.Length != Length)
        {
            canonical = string.Empty;
            return false;
        }

        foreach (var character in trimmed)
        {
            if (Alphabet.IndexOf(char.ToUpperInvariant(character)) < 0)
            {
                canonical = string.Empty;
                return false;
            }
        }

        canonical = trimmed.ToUpperInvariant();
        return true;
    }

    public override string ToString() => Value;
}
