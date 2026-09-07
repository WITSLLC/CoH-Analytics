using System.Windows.Media;

namespace CoHAnalytics.Services;

public interface ICustomCharacterIconService
{
    IReadOnlyList<CustomCharacterIcon> GetIcons();

    bool TryGetIcon(string iconId, out CustomCharacterIcon icon);

    CustomCharacterIconSaveResult SaveNormalizedIcon(byte[] pngData);

    CustomCharacterIconDeleteResult DeleteIcon(string iconId);
}

public sealed record CustomCharacterIcon
{
    public required string Id { get; init; }

    public required ImageSource ImageSource { get; init; }
}

public sealed record CustomCharacterIconSaveResult
{
    public bool IsSuccess { get; init; }

    public CustomCharacterIcon? Icon { get; init; }

    public string? ErrorMessage { get; init; }

    public static CustomCharacterIconSaveResult Success(CustomCharacterIcon icon) =>
        new() { IsSuccess = true, Icon = icon };

    public static CustomCharacterIconSaveResult Failure(string message) =>
        new() { ErrorMessage = message };
}

public sealed record CustomCharacterIconDeleteResult
{
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public static CustomCharacterIconDeleteResult Success() =>
        new() { IsSuccess = true };

    public static CustomCharacterIconDeleteResult Failure(string message) =>
        new() { ErrorMessage = message };
}
