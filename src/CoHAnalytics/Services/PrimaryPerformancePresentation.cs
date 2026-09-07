using System.Globalization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Builds Live Session primary performance rows from authoritative combat snapshots.
/// </summary>
public static class PrimaryPerformancePresentation
{
  private const string UnavailableLabel = "—";

  public static PrimaryPerformanceMetric BuildSession(
      CombatSnapshot combat,
      TimeSpan elapsed,
      bool hasCombatData)
  {
    var descriptor = PrimaryPerformanceMetric.BetaDamage;

    if (!hasCombatData)
    {
      return descriptor with
      {
        RateValue = UnavailableLabel,
        TotalValue = UnavailableLabel,
        AvailabilityState = PrimaryPerformanceAvailabilityState.NoData
      };
    }

    var totalValue = FormatCombatDamageTotal(combat.DamageDealt);

    if (!GameplaySessionTelemetryPresentation.CanShowRates(elapsed))
    {
      return descriptor with
      {
        RateValue = UnavailableLabel,
        TotalValue = totalValue,
        AvailabilityState = PrimaryPerformanceAvailabilityState.WarmUp
      };
    }

    return descriptor with
    {
      RateValue = FormatDamagePerSecond(combat.SessionDamagePerSecondHundredths),
      TotalValue = totalValue,
      AvailabilityState = PrimaryPerformanceAvailabilityState.Available
    };
  }

  public static PrimaryPerformanceMetric BuildTracked(TrackedCombatScopeSnapshot tracked)
  {
    var descriptor = PrimaryPerformanceMetric.BetaDamage;

    if (!tracked.IsTracking)
    {
      return descriptor with
      {
        RateValue = UnavailableLabel,
        TotalValue = UnavailableLabel,
        AvailabilityState = PrimaryPerformanceAvailabilityState.Inactive
      };
    }

    var totalValue = FormatCombatDamageTotal(tracked.DamageDealt);

    if (!GameplaySessionTelemetryPresentation.CanShowRates(tracked.ActiveElapsed))
    {
      return descriptor with
      {
        RateValue = UnavailableLabel,
        TotalValue = totalValue,
        AvailabilityState = PrimaryPerformanceAvailabilityState.WarmUp
      };
    }

    return descriptor with
    {
      RateValue = FormatDamagePerSecond(tracked.DamagePerSecondHundredths),
      TotalValue = totalValue,
      AvailabilityState = PrimaryPerformanceAvailabilityState.Available
    };
  }

  public static string FormatDamagePerSecond(
      long damagePerSecondHundredths,
      IFormatProvider? formatProvider = null)
  {
    if (damagePerSecondHundredths <= 0)
    {
      return "0 DPS";
    }

    var dps = damagePerSecondHundredths / 100.0;
    return $"{FormatCompactPerSecondValue(dps, formatProvider)} DPS";
  }

  public static string FormatCombatDamageTotal(
      CombatScaledAmount amount,
      IFormatProvider? formatProvider = null)
  {
    if (amount.Hundredths <= 0)
    {
      return "0";
    }

    if (amount.Hundredths % CombatScaledAmount.Scale != 0)
    {
      return amount.ToString();
    }

    return FormatCompactQuantity(amount.Hundredths / CombatScaledAmount.Scale, formatProvider);
  }

  internal static string FormatCompactPerSecondValue(double value, IFormatProvider? formatProvider = null)
  {
    var culture = formatProvider ?? CultureInfo.CurrentCulture;

    if (value >= 1_000_000)
    {
      var millions = value / 1_000_000.0;
      return millions >= 10
          ? $"{millions.ToString("N0", culture)}M"
          : $"{millions.ToString("0.#", culture)}M";
    }

    if (value >= 10_000)
    {
      var thousands = value / 1_000.0;
      return thousands >= 100
          ? $"{thousands.ToString("N0", culture)}K"
          : $"{thousands.ToString("0.#", culture)}K";
    }

    if (value >= 1_000)
    {
      var thousands = value / 1_000.0;
      return $"{thousands.ToString("0.#", culture)}K";
    }

    return value.ToString("N0", culture);
  }

  internal static string FormatCompactQuantity(long value, IFormatProvider? formatProvider = null)
  {
    var culture = formatProvider ?? CultureInfo.CurrentCulture;

    if (value >= 1_000_000)
    {
      var millions = value / 1_000_000.0;
      return millions >= 10
          ? $"{millions.ToString("N0", culture)}M"
          : $"{millions.ToString("0.#", culture)}M";
    }

    if (value >= 10_000)
    {
      var thousands = value / 1_000.0;
      return $"{thousands.ToString("N0", culture)}K";
    }

    if (value >= 1_000)
    {
      var thousands = value / 1_000.0;
      return $"{thousands.ToString("0.#", culture)}K";
    }

    return value.ToString("N0", culture);
  }
}
