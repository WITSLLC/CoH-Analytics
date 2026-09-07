namespace CoHAnalytics.Models;

/// <summary>
/// Versioned persisted gameplay or tracked session snapshot. Recent sessions live under
/// <c>Sessions\Live</c> or <c>Sessions\Tracked</c>; promoted copies live under <c>Sessions\Saved</c>.
/// </summary>
public sealed record PersistedSessionDocument
{
    public const int MinimumSupportedSchemaVersion = 1;

    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid SessionId { get; init; }

    public required SessionType SessionType { get; init; }

    public string? Account { get; init; }

    public string? Character { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset EndedAtUtc { get; init; }

    public long DurationSeconds { get; init; }

    public SessionEarnings Earnings { get; init; } = new();

    /// <summary>Non-zero reward currency totals observed for this session snapshot.</summary>
    public IReadOnlyList<PersistedSessionCurrencyTotal> Currencies { get; init; } = [];

    public PersistedSessionLootTotals Loot { get; init; } = new();

    public SessionContextMetadata Context { get; init; } = new();

    /// <summary>When this record was promoted into permanent Saved storage.</summary>
    public DateTimeOffset? SavedAtUtc { get; init; }
}

public sealed record SessionEarnings
{
    public long Experience { get; init; }

    public long Influence { get; init; }
}

/// <summary>One recognized currency total persisted with the session.</summary>
public sealed record PersistedSessionCurrencyTotal
{
    public required string Name { get; init; }

    public long Quantity { get; init; }
}

/// <summary>Per-family item totals for salvage, recipes, enhancements, and inspirations.</summary>
public sealed record PersistedSessionLootTotals
{
    public IReadOnlyList<PersistedSessionLootItem> Salvage { get; init; } = [];

    public IReadOnlyList<PersistedSessionLootItem> Recipes { get; init; } = [];

    public IReadOnlyList<PersistedSessionLootItem> Enhancements { get; init; } = [];

    public IReadOnlyList<PersistedSessionLootItem> Inspirations { get; init; } = [];
}

/// <summary>One observed item name and quantity. Catalog identity is optional when available.</summary>
public sealed record PersistedSessionLootItem
{
    public required string Name { get; init; }

    public long Quantity { get; init; }

    public string? CatalogId { get; init; }
}

/// <summary>Optional user-editable context metadata. Defaults are empty until an editor exists.</summary>
public sealed record SessionContextMetadata
{
    public string? Title { get; init; }

    public string? Category { get; init; }

    public string? Content { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool Favorite { get; init; }
}
