namespace CoHAnalytics.Services;

/// <summary>Code-defined conservative structural limits; these are not user settings.</summary>
public sealed class ParserClassificationOptions
{
    public int MaximumChannelLength { get; init; } = 64;
    public int MaximumSpeakerLength { get; init; } = 64;
    public int MaximumCandidateNameLength { get; init; } = 64;

    internal void Validate()
    {
        if (MaximumChannelLength <= 0 || MaximumSpeakerLength <= 0 || MaximumCandidateNameLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ParserClassificationOptions));
        }
    }
}
