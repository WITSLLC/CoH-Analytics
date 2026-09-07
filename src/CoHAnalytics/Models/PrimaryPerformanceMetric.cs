namespace CoHAnalytics.Models;

/// <summary>
/// Descriptor-shaped read model for the one variable performance row on Live Session.
/// Beta resolves exactly one metric: damage dealt / DPS.
/// </summary>
public sealed record PrimaryPerformanceMetric
{
  public static PrimaryPerformanceMetric BetaDamage { get; } = new(
      "DPS",
      "Damage Dealt",
      PrimaryPerformanceMetricKind.Damage);

  private PrimaryPerformanceMetric(
      string rateLabel,
      string totalLabel,
      PrimaryPerformanceMetricKind metric)
  {
    RateLabel = rateLabel;
    TotalLabel = totalLabel;
    Metric = metric;
  }

  public string RateLabel { get; }

  public string TotalLabel { get; }

  public PrimaryPerformanceMetricKind Metric { get; }

  public string RateValue { get; init; } = "—";

  public string TotalValue { get; init; } = "—";

  public PrimaryPerformanceAvailabilityState AvailabilityState { get; init; } =
      PrimaryPerformanceAvailabilityState.NoData;
}

/// <summary>Beta primary performance metric selection. Only <see cref="Damage"/> is implemented.</summary>
public enum PrimaryPerformanceMetricKind
{
  Damage
}
