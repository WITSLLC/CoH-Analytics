namespace CoHAnalytics.Models;

/// <summary>Four-mode proc-parent attribution. Default is the safe unset/unattributed state.</summary>
public enum ProcAttributionMode
{
    Unattributed = 0,
    Direct = 1,
    BuildConfirmed = 2,
    Correlated = 3
}

/// <summary>Which attribution path produced a classification.</summary>
public enum ProcAttributionPath
{
    None = 0,
    AttackPower = 1,
    ProcParent = 2,
    GlobalEffect = 3,
    OwnedPet = 4
}

/// <summary>Result of mapping a raw enhancement token through the capture-time catalog.</summary>
public enum ProcMappingStatus
{
    Unknown = 0,
    Resolved = 1,
    Collision = 2,
    Incomplete = 3
}

/// <summary>Stable slot occurrence. Count these, not distinct parent powers.</summary>
public sealed record SlotOccurrenceKey
{
    public required string RawPowerToken { get; init; }

    public required int PowerSourceOrder { get; init; }

    public required int SlotOrder { get; init; }

    public override string ToString() =>
        $"{RawPowerToken}#{PowerSourceOrder}:{SlotOrder}";
}

/// <summary>One frozen power row. Tokens are the analytical identity; display names are derived.</summary>
public sealed record FrozenBuildPower
{
    public required string RawPowerToken { get; init; }

    public required string RawPowerSetToken { get; init; }

    public required string RawCategoryToken { get; init; }

    public required int AcquisitionLevel { get; init; }

    public required int SourceOrder { get; init; }

    /// <summary>Qualified raw build identity; never a reverse lookup from a log display name.</summary>
    public string CanonicalPowerId => $"{RawCategoryToken}.{RawPowerSetToken}.{RawPowerToken}";

    public string SurfacedPowerName => RawPowerToken.Replace("_", " ", StringComparison.Ordinal);
}

/// <summary>One frozen slot with capture-time mapping facts. Not a live catalog pointer.</summary>
public sealed record FrozenProcSlot
{
    public required SlotOccurrenceKey Occurrence { get; init; }

    public string? EnhancementToken { get; init; }

    public string? CanonicalEnhancementId { get; init; }

    public string? ExactProcIdentity { get; init; }

    public string? EnhancementSetId { get; init; }

    public string? SlottedInPowerId { get; init; }

    public bool IsAttuned { get; init; }

    public bool IsEmpty { get; init; }

    public bool IsGlobalOrIncarnate { get; init; }

    public ProcMappingStatus MappingStatus { get; init; }

    public MetricEvidence MappingEvidence { get; init; }
}

/// <summary>
/// Immutable analytical build/proc context. Hash covers semantic mapping content only:
/// powers, slots, resolved ids, and catalog fingerprint. Capture time, source path,
/// sync time, and record ownership are excluded.
/// </summary>
public sealed record FrozenBuildManifest
{
    static FrozenBuildManifest()
    {
        Empty = FrozenBuildManifestHash.Finalize(new FrozenBuildManifest
        {
            ManifestHash = string.Empty,
            Powers = [],
            ProcSlots = []
        });
    }

    public static FrozenBuildManifest Empty { get; }

    public required string ManifestHash { get; init; }

    public string? BuildCatalogFingerprint { get; init; }

    public int AttributionPolicyVersion { get; init; } = Models.AttributionPolicyVersion.Current;

    private IReadOnlyList<FrozenBuildPower> _powers = [];
    private IReadOnlyList<FrozenProcSlot> _slots = [];
    private IReadOnlyList<FrozenProcIdentity> _identities = [];
    public IReadOnlyList<FrozenBuildPower> Powers
    {
        get => _powers;
        init => _powers = Array.AsReadOnly(value.ToArray());
    }
    public IReadOnlyList<FrozenProcSlot> ProcSlots
    {
        get => _slots;
        init => _slots = Array.AsReadOnly(value.ToArray());
    }
    /// <summary>Capture-time validated names, including identity collisions; no future catalog needed.</summary>
    public IReadOnlyList<FrozenProcIdentity> ProcIdentities
    {
        get => _identities;
        init => _identities = Array.AsReadOnly(value.ToArray());
    }
    public bool HasCompleteMapping => Powers.Count > 0
        && Powers.All(power => !string.IsNullOrWhiteSpace(power.RawPowerToken)
            && !string.IsNullOrWhiteSpace(power.RawCategoryToken) && !string.IsNullOrWhiteSpace(power.RawPowerSetToken))
        && Powers.Select(power => power.SourceOrder).Distinct().Count() == Powers.Count
        && Powers.Select(power => power.CanonicalPowerId).Distinct(StringComparer.Ordinal).Count() == Powers.Count
        && ProcSlots.Select(slot => slot.Occurrence).Distinct().Count() == ProcSlots.Count
        && ProcSlots.All(slot => (slot.IsEmpty || slot.MappingStatus == ProcMappingStatus.Resolved)
            && Powers.Any(power => power.SourceOrder == slot.Occurrence.PowerSourceOrder
                && power.RawPowerToken == slot.Occurrence.RawPowerToken && power.CanonicalPowerId == slot.SlottedInPowerId));
}

/// <summary>Parent-power attribution for one logical proc or attack occurrence.</summary>
public sealed record PowerAttribution
{
    public static PowerAttribution Unattributed { get; } = new()
    {
        Mode = ProcAttributionMode.Unattributed,
        Path = ProcAttributionPath.None,
        PolicyVersion = Models.AttributionPolicyVersion.Current
    };

    public required ProcAttributionMode Mode { get; init; }

    public ProcAttributionPath Path { get; init; }

    public string? ParentPowerId { get; init; }

    public string? ParentPowerName { get; init; }

    public string? ExactProcIdentity { get; init; }

    private IReadOnlyList<ProcAttributionCandidate> _candidates = [];
    public IReadOnlyList<ProcAttributionCandidate> Candidates
    {
        get => _candidates;
        init => _candidates = Array.AsReadOnly(value.ToArray());
    }

    public MetricEvidence Evidence { get; init; }

    public MetricConfidence? Confidence { get; init; }

    public int PolicyVersion { get; init; } = Models.AttributionPolicyVersion.Current;

    public string? ManifestHash { get; init; }
}

/// <summary>A remaining parent candidate when uniqueness fails.</summary>
public sealed record ProcAttributionCandidate
{
    public required string ParentPowerId { get; init; }

    public required string ParentPowerName { get; init; }

    public required SlotOccurrenceKey Occurrence { get; init; }
}

/// <summary>Exact log name and stable catalog identity validated at capture; duplicates retain ambiguity.</summary>
public sealed record FrozenProcIdentity(string LogName, string CatalogItemId);
