namespace CoHAnalytics.Models;

/// <summary>
/// Immutable, source-ordered snapshot of the build-layout portion of a Homecoming
/// <c>/buildsavefile</c> export.
/// </summary>
public sealed class HomecomingBuildLayoutSnapshot
{
    internal HomecomingBuildLayoutSnapshot(
        string? characterName,
        int? characterLevel,
        string? rawClassToken,
        IEnumerable<HomecomingBuildPowerSnapshot> powers)
    {
        CharacterName = characterName;
        CharacterLevel = characterLevel;
        RawClassToken = rawClassToken;
        Powers = Array.AsReadOnly(powers.ToArray());
    }

    public string? CharacterName { get; }

    public int? CharacterLevel { get; }

    public string? RawClassToken { get; }

    public IReadOnlyList<HomecomingBuildPowerSnapshot> Powers { get; }
}

/// <summary>A raw power selection in its original buildsave position.</summary>
public sealed class HomecomingBuildPowerSnapshot
{
    internal HomecomingBuildPowerSnapshot(
        int acquisitionLevel,
        string rawCategoryToken,
        string rawPowerSetToken,
        string rawPowerToken,
        int sourceOrder,
        IEnumerable<HomecomingBuildSlotSnapshot> slots)
    {
        AcquisitionLevel = acquisitionLevel;
        RawCategoryToken = rawCategoryToken;
        RawPowerSetToken = rawPowerSetToken;
        RawPowerToken = rawPowerToken;
        SourceOrder = sourceOrder;
        Slots = Array.AsReadOnly(slots.ToArray());
    }

    public int AcquisitionLevel { get; }

    public string RawCategoryToken { get; }

    public string RawPowerSetToken { get; }

    public string RawPowerToken { get; }

    /// <summary>Zero-based occurrence order among power lines in the source file.</summary>
    public int SourceOrder { get; }

    public IReadOnlyList<HomecomingBuildSlotSnapshot> Slots { get; }
}

/// <summary>A raw slot entry in its original position beneath a power.</summary>
public sealed class HomecomingBuildSlotSnapshot
{
    internal HomecomingBuildSlotSnapshot(
        bool isEmpty,
        string? rawEnhancementToken,
        bool isAttuned,
        int? baseEnhancementLevel,
        int? boostValue,
        int slotOrder)
    {
        IsEmpty = isEmpty;
        RawEnhancementToken = rawEnhancementToken;
        IsAttuned = isAttuned;
        BaseEnhancementLevel = baseEnhancementLevel;
        BoostValue = boostValue;
        SlotOrder = slotOrder;
    }

    public bool IsEmpty { get; }

    public string? RawEnhancementToken { get; }

    public bool IsAttuned { get; }

    public int? BaseEnhancementLevel { get; }

    public int? BoostValue { get; }

    /// <summary>Zero-based occurrence order among slot lines for the containing power.</summary>
    public int SlotOrder { get; }
}
