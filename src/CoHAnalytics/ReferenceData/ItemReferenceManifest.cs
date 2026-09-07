namespace CoHAnalytics.ReferenceData;

/// <summary>Version and compatibility metadata for a shipped item reference catalog package.</summary>
public sealed record ItemReferenceManifest
{
    public required string CatalogVersion { get; init; }

    public required ItemReferenceHomecomingCompatibility HomecomingCompatibility { get; init; }

    public string? SourceRevision { get; init; }

    public string? SourceNotes { get; init; }
}

/// <summary>Homecoming client build range validated against this catalog package.</summary>
public sealed record ItemReferenceHomecomingCompatibility
{
    public string? BuildMin { get; init; }

    public string? BuildMax { get; init; }
}
