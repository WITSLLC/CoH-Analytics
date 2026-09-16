namespace CoHAnalytics.Models;

/// <summary>Versions on-disk Segment file/field layout. Independent of analytical semantics.</summary>
public static class SegmentSchemaVersion
{
    /// <summary>
    /// Multi-file hybrid layout: metadata, coverage, aggregates, gzip JSON spine, annotations,
    /// and content-addressed frozen manifests. First 1024 logical events retained in apply order.
    /// </summary>
    public const int Current = 1;
}

/// <summary>Versions the bounded normalized event spine payload inside <c>spine.bin</c>.</summary>
public static class SpineSchemaVersion
{
    public const int Current = 1;
}

/// <summary>Live and durable logical-event retention bound. Matches the live combat tail.</summary>
public static class SegmentSpineLimits
{
    public const int MaxRetainedLogicalEvents = 1024;
}
