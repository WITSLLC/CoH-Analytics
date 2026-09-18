using System.Globalization;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>Formatting and selection over supplied projections; no persistence or combat calculations.</summary>
public sealed partial class CombatOffenseViewModel(
    IHomecomingPowerReferenceCatalog? powerCatalog = null,
    IInstalledGameAssetProvider? assets = null) : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<OffenseValue> _summary = [];
    [ObservableProperty] private IReadOnlyList<OffensePower> _powers = [];
    [ObservableProperty] private OffensePower? _selectedPower;
    [ObservableProperty] private IReadOnlyList<OffenseTotal> _damageTypes = [];
    [ObservableProperty] private IReadOnlyList<OffenseTotal> _targets = [];
    [ObservableProperty] private IReadOnlyList<OffenseProc> _procs = [];
    [ObservableProperty] private string? _coverageNote;
    [ObservableProperty] private string? _damageTypesNote;
    [ObservableProperty] private string? _procsNote;
    [ObservableProperty] private string _emptyMessage = "Select a historical segment to view offense.";

    public bool HasPowers => Powers.Count > 0;
    public bool HasDamageTypes => DamageTypes.Count > 0;
    public bool HasTargets => Targets.Count > 0;
    public bool HasProcs => Procs.Count > 0;
    public bool ShowEmptyMessage => Summary.Count == 0 && !HasPowers && !HasDamageTypes && !HasTargets && !HasProcs;
    public bool HasSelectedPower => SelectedPower is not null;

    partial void OnSelectedPowerChanged(OffensePower? value)
    {
        if (value is not null && !Powers.Contains(value)) { SelectedPower = null; return; }
        OnPropertyChanged(nameof(HasSelectedPower));
    }

    public void SetProjection(CombatAnalyticsProjection? projection, FrozenBuildManifest? manifest = null)
    {
        SelectedPower = null;
        Summary = [];
        Powers = [];
        DamageTypes = [];
        Targets = [];
        Procs = [];
        CoverageNote = DamageTypesNote = ProcsNote = null;
        EmptyMessage = projection is null ? "Select a historical segment to view offense."
            : "No offense details were captured for this segment.";
        if (projection is { } p)
        {
            var summary = new List<OffenseValue>();
            Add(summary, "Total outgoing damage", p.Session.Metrics.DamageDealt, Amount);
            Add(summary, "Player damage", p.Session.Metrics.DamageDealtSelf, Amount);
            Add(summary, "Proc damage", p.Attribution.ProcDamage, Amount);
            Add(summary, "Session DPS", p.Clock.WallClockDamagePerSecondHundredths,
                v => new CombatScaledAmount(v).ToString(), "Based on total elapsed session time.");
            Summary = summary;
            CoverageNote = p.CoverageLimited || p.Session.CoverageLimited ? Partial : null;
            DamageTypes = TypeRows(p.DamageTypeBreakdown);
            DamageTypesNote = Note(p.DamageTypeBreakdown.Availability);
            Targets = p.Targets.Where(r => r.DamageDealt.Hundredths != 0 || r.EventCount != 0)
                .OrderByDescending(r => r.DamageDealt.Hundredths)
                .ThenBy(r => r.DisplayName ?? r.NormalizedTargetName, StringComparer.Ordinal)
                .Select(r => new OffenseTotal(r.IsOverflow ? "Additional targets" : r.DisplayName ?? r.NormalizedTargetName,
                    Amount(r.DamageDealt), Count(r.EventCount), r.IsOverflow ? "Additional targets grouped." : null)).ToArray();
            Procs = p.Attribution.ByParent.Where(r => Visible(r.ProcDamageMetric))
                .OrderByDescending(r => r.ProcDamageMetric.Value!.Value.Hundredths)
                .Select(r => new OffenseProc(r,
                    DisplayIdentity(r.ExactProcIdentity) ?? "Proc damage",
                    r.ParentPowerName ?? "Unattributed",
                    Amount(r.ProcDamageMetric.Value!.Value), Count(r.EventCount), Note(r.ProcDamageMetric.Availability))).ToArray();
            ProcsNote = p.Attribution.ParentRowsIncomplete ? "Partial — some proc parents could not be identified." : null;
            Powers = p.Powers.Where(r => r.Direction == CombatAnalyticsDirection.Outgoing
                    && r.Scope != CombatAnalyticsScope.PerPet
                    && (Visible(r.DamageMagnitudeMetric) || r.ActivationCount > 0
                        || r.ConfirmedStillRechargingCount > 0 || r.ConfirmedRechargeCompletedCount > 0))
                .OrderByDescending(r => Visible(r.DamageMagnitudeMetric) ? r.DamageMagnitudeMetric.Value!.Value.Hundredths : -1)
                .ThenBy(r => r.PowerName, StringComparer.Ordinal).ThenBy(r => r.Scope)
                .Select(r => Power(r, manifest)).ToArray();
            SelectedPower = Powers.FirstOrDefault();
        }
        foreach (var name in new[] { nameof(HasPowers), nameof(HasDamageTypes), nameof(HasTargets), nameof(HasProcs), nameof(ShowEmptyMessage) })
            OnPropertyChanged(name);
    }

    private OffensePower Power(CombatPowerAnalysisRow row, FrozenBuildManifest? manifest)
    {
        // Only an unambiguous captured build identity can supply an icon/parent identity.
        // Never guess a category or reverse-map names through the current catalog.
        var matches = row.Scope == CombatAnalyticsScope.Self && !row.IsOverflow
            ? manifest?.Powers.Where(p => string.Equals(p.SurfacedPowerName, row.PowerName, StringComparison.OrdinalIgnoreCase)).ToArray() ?? []
            : [];
        var power = matches.Length == 1 ? matches[0] : null;
        ImageSource? icon = null;
        if (power is not null && powerCatalog?.TryResolve(power.RawCategoryToken, power.RawPowerSetToken,
                power.RawPowerToken, out var reference) == true && reference.IconIdentity is not null)
            icon = assets?.TryResolve(reference.IconIdentity);

        var details = new List<OffenseValue>();
        Add(details, "Damage", row.DamageMagnitudeMetric, Amount);
        if (Visible(row.DamageMagnitudeMetric))
        {
            details.Add(new("Direct damage", Amount(row.DirectAmount)));
            details.Add(new("DoT damage", Amount(row.DotAmount)));
            if (row.LargestHit is { } largest) details.Add(new("Maximum hit", Amount(largest)));
        }
        // Untyped lifecycle counters have no observed-zero flag; show only recorded counts.
        if (row.ActivationCount > 0) details.Add(new("Activations", Count(row.ActivationCount)));
        if (row.EventCount > 0) details.Add(new("Events", Count(row.EventCount)));
        if (row.ConfirmedStillRechargingCount > 0) details.Add(new("Still recharging", Count(row.ConfirmedStillRechargingCount)));
        if (row.ConfirmedRechargeCompletedCount > 0) details.Add(new("Recharge completed", Count(row.ConfirmedRechargeCompletedCount)));
        Add(details, "Distinct targets", row.DistinctTargetCountMetric, Count);
        return new OffensePower(row, row.IsOverflow ? "Additional powers" : row.PowerName,
            row.Scope == CombatAnalyticsScope.Self ? "Player" : "Owned pets", icon,
            Visible(row.DamageMagnitudeMetric) ? Amount(row.DamageMagnitudeMetric.Value!.Value) : null,
            row.ActivationCount > 0 ? Count(row.ActivationCount) : null,
            row.EventCount > 0 ? Count(row.EventCount) : null,
            row.IsOverflow ? "Additional powers grouped." : row.CoverageLimited ? Partial : Note(row.DamageMagnitudeMetric.Availability),
            details, TypeRows(row.DamageTypeBreakdown), Note(row.DamageTypeBreakdown.Availability),
            power is null ? [] : Procs.Where(p => p.Source.ParentPowerId == power.CanonicalPowerId).ToArray());
    }

    private static IReadOnlyList<OffenseTotal> TypeRows(MetricRef<IReadOnlyList<CombatDamageTypeTotal>> metric) =>
        Visible(metric) ? metric.Value!.OrderByDescending(r => r.Amount.Hundredths)
            .ThenBy(r => r.DamageType.Text, StringComparer.Ordinal)
            .Select(r => new OffenseTotal(r.IsOverflow ? "Additional damage types" :
                (r.DamageType.IsUnresistable ? "Unresistable " : "") + r.DamageType.Text,
                Amount(r.Amount), Count(r.EventCount), r.IsOverflow ? "Additional damage types grouped." : null)).ToArray() : [];

    private static bool Visible<T>(Metric<T> metric) where T : struct => metric.Value.HasValue
        && metric.Availability is MetricAvailability.Available or MetricAvailability.Incomplete;
    private static bool Visible<T>(MetricRef<T> metric) where T : class => metric.Value is not null
        && metric.Availability is MetricAvailability.Available or MetricAvailability.Incomplete;
    private static string Amount(CombatScaledAmount value) => value.ToString();
    private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private static string? DisplayIdentity(string? value) => value?.Replace('_', ' ');
    private const string Partial = "Partial — some activity may not have been captured.";
    private static string? Note(MetricAvailability availability) => availability == MetricAvailability.Incomplete ? Partial : null;
    private static void Add<T>(List<OffenseValue> values, string label, Metric<T> metric, Func<T, string> format, string? note = null) where T : struct
    {
        if (Visible(metric)) values.Add(new(label, format(metric.Value!.Value),
            string.Join(" ", new[] { Note(metric.Availability), note }.Where(n => n is not null))));
    }
}

public sealed record OffenseValue(string Label, string Value, string? Note = null);
public sealed record OffenseTotal(string Label, string Damage, string Events, string? Note);
public sealed record OffenseProc(CombatProcParentRow Source, string Label, string Parent, string Damage, string Events, string? Note);
public sealed record OffensePower(CombatPowerAnalysisRow Source, string Name, string Scope, ImageSource? Icon,
    string? Damage, string? Activations, string? Events, string? Note, IReadOnlyList<OffenseValue> Details,
    IReadOnlyList<OffenseTotal> DamageTypes, string? DamageTypesNote, IReadOnlyList<OffenseProc> Procs)
{
    public bool HasDamageTypes => DamageTypes.Count > 0;
    public bool HasProcs => Procs.Count > 0;
}
