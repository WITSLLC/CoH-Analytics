using System.Globalization;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>Formatting and selection over supplied incoming projection rows; no persistence or combat calculations.</summary>
public sealed partial class CombatIncomingViewModel(
    IHomecomingPowerReferenceCatalog? powerCatalog = null,
    IInstalledGameAssetProvider? assets = null,
    IItemReferenceCatalog? items = null,
    IEnhancementIconCompositor? compositor = null,
    IHomecomingBoostMetadataProvider? boostMetadata = null) : ObservableObject
{
    private readonly CombatRowIconSupport _icons = new(powerCatalog, assets, items, compositor, boostMetadata);
    private IncomingPower[] _powerRows = [];

    [ObservableProperty] private IReadOnlyList<OffenseValue> _summary = [];
    [ObservableProperty] private IReadOnlyList<IncomingPower> _powers = [];
    [ObservableProperty] private IncomingPower? _selectedPower;
    [ObservableProperty] private IReadOnlyList<OffenseTotal> _damageTypes = [];
    [ObservableProperty] private IReadOnlyList<OffenseTotal> _largestHits = [];
    [ObservableProperty] private string? _coverageNote;
    [ObservableProperty] private string? _damageTypesNote;
    [ObservableProperty] private string _emptyMessage = "Select a historical segment to view incoming.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerColumnHeader))]
    [NotifyPropertyChangedFor(nameof(TargetColumnHeader))]
    [NotifyPropertyChangedFor(nameof(DamageColumnHeader))]
    [NotifyPropertyChangedFor(nameof(EventsColumnHeader))]
    private string _sortedColumn = "Damage";

    public string PowerColumnHeader => ColumnHeader("Power");
    public string TargetColumnHeader => ColumnHeader("Target");
    public string DamageColumnHeader => ColumnHeader("Damage");
    public string EventsColumnHeader => ColumnHeader("Events");

    public bool HasPowers => Powers.Count > 0;
    public bool HasDamageTypes => DamageTypes.Count > 0;
    public bool HasLargestHits => LargestHits.Count > 0;
    public bool ShowEmptyMessage => Summary.Count == 0 && !HasPowers && !HasDamageTypes && !HasLargestHits;
    public bool HasSelectedPower => SelectedPower is not null;

    partial void OnSelectedPowerChanged(IncomingPower? value)
    {
        if (value is not null && !Powers.Contains(value)) { SelectedPower = null; return; }
        OnPropertyChanged(nameof(HasSelectedPower));
    }

    public void SetProjection(CombatAnalyticsProjection? projection, FrozenBuildManifest? manifest = null)
    {
        SelectedPower = null;
        Summary = [];
        Powers = [];
        _powerRows = [];
        SortedColumn = "Damage";
        DamageTypes = [];
        LargestHits = [];
        CoverageNote = DamageTypesNote = null;
        EmptyMessage = projection is null ? "Select a historical segment to view incoming."
            : "No incoming details were captured for this segment.";
        if (projection is { } p)
        {
            var summary = new List<OffenseValue>();
            var sectionPartial = p.CoverageLimited || p.Session.CoverageLimited;
            CoverageNote = sectionPartial ? Partial : null;
            Add(summary, "Damage taken", p.Session.Metrics.DamageReceived, Amount, includeAvailabilityNote: !sectionPartial);
            Add(summary, "Pet damage taken", p.Session.Metrics.DamageReceivedOwnedPets, Amount, includeAvailabilityNote: !sectionPartial);
            Summary = summary;
            DamageTypes = TypeRows(p.IncomingDamageTypeBreakdown);
            DamageTypesNote = Note(p.IncomingDamageTypeBreakdown.Availability);
            _powerRows = p.Powers.Where(r => r.Direction == CombatAnalyticsDirection.Incoming
                    && r.Scope != CombatAnalyticsScope.PerPet
                    && Visible(r.DamageMagnitudeMetric)
                    && r.DamageMagnitudeMetric.Value!.Value.Hundredths > 0)
                .Select(r => Power(r, manifest)).ToArray();
            LargestHits = _powerRows.Where(r => r.Source.LargestHit is not null)
                .OrderByDescending(r => r.Source.LargestHit!.Value.Hundredths)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .ThenBy(r => r.Source.Scope)
                .Select(r => new OffenseTotal(r.Name, Amount(r.Source.LargestHit!.Value),
                    Count(r.Source.EventCount), r.Scope == "Owned pets" ? "Owned pets" : r.Source.IsOverflow ? "Additional powers grouped." : null))
                .ToArray();
            ApplySort("Damage");
            SelectedPower = Powers.FirstOrDefault();
        }
        foreach (var name in new[] { nameof(HasPowers), nameof(HasDamageTypes), nameof(HasLargestHits), nameof(ShowEmptyMessage) })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void SortPowers(string? column)
    {
        if (column is not ("Power" or "Target" or "Damage" or "Events")) return;
        var selected = SelectedPower;
        ApplySort(column);
        if (selected is not null && Powers.Contains(selected)) SelectedPower = selected;
    }

    private void ApplySort(string column)
    {
        SortedColumn = column;
        IOrderedEnumerable<IncomingPower> ordered = column switch
        {
            "Power" => _powerRows.OrderByDescending(p => p.Name, StringComparer.Ordinal)
                .ThenByDescending(DamageHundredths),
            "Target" => _powerRows.OrderByDescending(p => p.Scope, StringComparer.Ordinal)
                .ThenByDescending(DamageHundredths),
            "Events" => _powerRows.OrderByDescending(p => p.Source.EventCount)
                .ThenByDescending(DamageHundredths),
            _ => _powerRows.OrderByDescending(DamageHundredths)
        };
        Powers = ordered.ThenBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Source.Scope).ToArray();
    }

    private static long DamageHundredths(IncomingPower power) =>
        power.Source.DamageMagnitudeMetric.Value!.Value.Hundredths;

    private string ColumnHeader(string column) =>
        string.Equals(SortedColumn, column, StringComparison.Ordinal) ? column + " ▾" : column;

    private IncomingPower Power(CombatPowerAnalysisRow row, FrozenBuildManifest? manifest)
    {
        var icon = _icons.Resolve(row, frozen: null, manifest);
        var details = new List<OffenseValue>();
        Add(details, "Damage", row.DamageMagnitudeMetric, Amount);
        if (Visible(row.DamageMagnitudeMetric))
        {
            details.Add(new("Direct damage", Amount(row.DirectAmount)));
            details.Add(new("DoT damage", Amount(row.DotAmount)));
            if (row.LargestHit is { } largest) details.Add(new("Maximum hit", Amount(largest)));
        }
        if (row.ActivationCount > 0) details.Add(new("Activations", Count(row.ActivationCount)));
        if (row.EventCount > 0) details.Add(new("Events", Count(row.EventCount)));
        if (row.ConfirmedStillRechargingCount > 0) details.Add(new("Still recharging", Count(row.ConfirmedStillRechargingCount)));
        if (row.ConfirmedRechargeCompletedCount > 0) details.Add(new("Recharge completed", Count(row.ConfirmedRechargeCompletedCount)));
        return new IncomingPower(row, row.IsOverflow ? "Additional powers" : row.PowerName,
            row.Scope == CombatAnalyticsScope.Self ? "Player" : "Owned pets", icon,
            Visible(row.DamageMagnitudeMetric) ? Amount(row.DamageMagnitudeMetric.Value!.Value) : null,
            row.EventCount > 0 ? Count(row.EventCount) : null,
            row.IsOverflow ? "Additional powers grouped." : row.CoverageLimited ? Partial : Note(row.DamageMagnitudeMetric.Availability),
            details, TypeRows(row.DamageTypeBreakdown), Note(row.DamageTypeBreakdown.Availability));
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
    private static string Amount(CombatScaledAmount value) =>
        (value.Hundredths / (decimal)CombatScaledAmount.Scale).ToString("#,##0.00", CultureInfo.InvariantCulture);
    private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private const string Partial = "Partial — some activity may not have been captured.";
    private static string? Note(MetricAvailability availability) => availability == MetricAvailability.Incomplete ? Partial : null;
    private static void Add<T>(List<OffenseValue> values, string label, Metric<T> metric, Func<T, string> format,
        string? note = null, bool includeAvailabilityNote = true) where T : struct
    {
        if (!Visible(metric)) return;
        var parts = new[] { includeAvailabilityNote ? Note(metric.Availability) : null, note }
            .Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
        values.Add(new(label, format(metric.Value!.Value), parts.Length == 0 ? null : string.Join(" ", parts)));
    }
}

public sealed record IncomingPower(CombatPowerAnalysisRow Source, string Name, string Scope, ImageSource? Icon,
    string? Damage, string? Events, string? Note, IReadOnlyList<OffenseValue> Details,
    IReadOnlyList<OffenseTotal> DamageTypes, string? DamageTypesNote)
{
    public bool HasDamageTypes => DamageTypes.Count > 0;
}
