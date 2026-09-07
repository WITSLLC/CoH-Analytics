namespace CoHAnalytics.Models;

/// <summary>Presentation availability for one Live Session primary performance row.</summary>
public enum PrimaryPerformanceAvailabilityState
{
  /// <summary>Rate and total are authoritative domain values.</summary>
  Available,

  /// <summary>Elapsed time is below the shared minimum rate window.</summary>
  WarmUp,

  /// <summary>No qualifying combat data exists for this scope yet.</summary>
  NoData,

  /// <summary>Tracked scope is not active.</summary>
  Inactive
}
