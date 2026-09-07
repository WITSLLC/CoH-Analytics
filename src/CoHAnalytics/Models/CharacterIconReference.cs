namespace CoHAnalytics.Models;

public enum CharacterIconKind
{
    BuiltIn,
    Custom
}

/// <summary>
/// Stable app-owned character icon identity for built-in and normalized custom icons.
/// </summary>
public sealed record CharacterIconReference
{
    public required CharacterIconKind Kind { get; init; }

    public required string IconId { get; init; }

    public static CharacterIconReference BuiltIn(string iconId) =>
        new()
        {
            Kind = CharacterIconKind.BuiltIn,
            IconId = iconId
        };

    public static CharacterIconReference Custom(string iconId) =>
        new()
        {
            Kind = CharacterIconKind.Custom,
            IconId = iconId
        };
}
