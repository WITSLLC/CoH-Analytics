using System.Windows.Media;

namespace CoHAnalytics.Services;

public interface IBuiltInCharacterIconService
{
    IReadOnlyList<BuiltInCharacterIcon> Icons { get; }

    bool TryGetIcon(string iconId, out BuiltInCharacterIcon icon);
}

public sealed record BuiltInCharacterIcon
{
    public required string Id { get; init; }

    public required ImageSource ImageSource { get; init; }
}
