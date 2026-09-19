using System.Globalization;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>Formatting and selection over supplied healing/support projection rows; no persistence or combat calculations.</summary>
public sealed partial class CombatHealingViewModel(
    IHomecomingPowerReferenceCatalog? powerCatalog = null,
    IInstalledGameAssetProvider? assets = null,
    IItemReferenceCatalog? items = null,
    IEnhancementIconCompositor? compositor = null,
    IHomecomingBoostMetadataProvider? boostMetadata = null) : ObservableObject
{
    private readonly CombatRowIconSupport _icons = new(powerCatalog, assets, items, compositor, boostMetadata);
    private HealingPower[] _powerRows = [];

    [ObservableProperty] private IReadOnlyList<OffenseValue> _summary = [];
    [ObservableProperty] private IReadOnlyList<HealingPower> _powers = [];
    [ObservableProperty] private HealingPower? _selectedPower;
    [ObservableProperty] private IReadOnlyList<OffenseTotal> _healingByPower = [];
    [ObservableProperty] private IReadOnlyList<OffenseTotal> _enduranceByPower = [];
    [ObservableProperty] private string? _coverageNote;
    [ObservableProperty] private string _emptyMessage = "Select a historical segment to view healing.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerColumnHeader))]
    [NotifyPropertyChangedFor(nameof(DirectionColumnHeader))]
    [NotifyPropertyChangedFor(nameof(AmountColumnHeader))]
    [NotifyPropertyChangedFor(nameof(EventsColumnHeader))]
    private string _sortedColumn = "Amount";

    public string PowerColumnHeader => ColumnHeader("Power");
    public string DirectionColumnHeader => ColumnHeader("Direction");
    public string AmountColumnHeader => ColumnHeader("Amount");
    public string EventsColumnHeader => ColumnHeader("Events");

    public bool HasPowers => Powers.Count > 0;
    public bool HasHealingByPower => HealingByPower.Count > 0;
    public bool HasEnduranceByPower => EnduranceByPower.Count > 0;
    public bool ShowEmptyMessage => Summary.Count == 0 && !HasPowers && !HasHealingByPower && !HasEnduranceByPower;
    public bool HasSelectedPower => SelectedPower is not null;

    partial void OnSelectedPowerChanged(HealingPower? value)
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
        SortedColumn = "Amount";
        HealingByPower = [];
        EnduranceByPower = [];
        CoverageNote = null;
        EmptyMessage = projection is null ? "Select a historical segment to view healing."
            : "No player-originated healing or support details were captured for this segment.";
        if (projection is { } p)
        {
            var sectionPartial = p.CoverageLimited || p.Session.CoverageLimited;
            CoverageNote = sectionPartial ? Partial : null;
            // Typed session support totals include pets; no availability-typed player-only totals exist.
            // Leave the summary empty rather than deriving totals from bounded power rows.
            var rows = new List<HealingPower>();
            foreach (var row in p.Powers)
            {
                if (row.Direction != CombatAnalyticsDirection.Outgoing || row.Scope != CombatAnalyticsScope.Self) continue;
                if (Visible(row.HealingMagnitudeMetric) && row.HealingMagnitudeMetric.Value!.Value.Hundredths > 0)
                    rows.Add(Power(row, manifest, endurance: false));
                if (Visible(row.EnduranceMagnitudeMetric) && row.EnduranceMagnitudeMetric.Value!.Value.Hundredths > 0)
                    rows.Add(Power(row, manifest, endurance: true));
            }
            _powerRows = [.. rows];
            HealingByPower = Totals(_powerRows.Where(r => !r.IsEndurance));
            EnduranceByPower = Totals(_powerRows.Where(r => r.IsEndurance));
            ApplySort("Amount");
            SelectedPower = Powers.FirstOrDefault();
        }
        foreach (var name in new[] { nameof(HasPowers), nameof(HasHealingByPower), nameof(HasEnduranceByPower), nameof(ShowEmptyMessage) })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void SortPowers(string? column)
    {
        if (column is not ("Power" or "Direction" or "Amount" or "Events")) return;
        var selected = SelectedPower;
        ApplySort(column);
        if (selected is not null && Powers.Contains(selected)) SelectedPower = selected;
    }

    private void ApplySort(string column)
    {
        SortedColumn = column;
        IOrderedEnumerable<HealingPower> ordered = column switch
        {
            "Power" => _powerRows.OrderByDescending(p => p.Name, StringComparer.Ordinal)
                .ThenByDescending(AmountHundredths),
            "Direction" => _powerRows.OrderByDescending(p => p.Direction, StringComparer.Ordinal)
                .ThenByDescending(AmountHundredths),
            "Events" => _powerRows.OrderByDescending(p => p.Source.EventCount)
                .ThenByDescending(AmountHundredths),
            _ => _powerRows.OrderByDescending(AmountHundredths)
        };
        Powers = ordered.ThenBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Direction, StringComparer.Ordinal)
            .ThenBy(p => p.Source.Scope).ToArray();
    }

    private static long AmountHundredths(HealingPower power) =>
        power.IsEndurance
            ? power.Source.EnduranceMagnitudeMetric.Value!.Value.Hundredths
            : power.Source.HealingMagnitudeMetric.Value!.Value.Hundredths;

    private string ColumnHeader(string column) =>
        string.Equals(SortedColumn, column, StringComparison.Ordinal) ? column + " ▾" : column;

    private HealingPower Power(CombatPowerAnalysisRow row, FrozenBuildManifest? manifest, bool endurance)
    {
        // Ownership was established by direction/scope; build matching supplies presentation only.
        var frozen = _icons.TryMatchFrozenPower(row, manifest);
        var icon = _icons.Resolve(row, frozen, manifest);
        var metric = endurance ? row.EnduranceMagnitudeMetric : row.HealingMagnitudeMetric;
        var direction = endurance ? "Endurance Granted" : "Healing Dealt";
        var amount = Amount(metric.Value!.Value);
        var details = new List<OffenseValue> { new(direction, amount) };
        if (row.ActivationCount > 0) details.Add(new("Activations", Count(row.ActivationCount)));
        if (row.EventCount > 0) details.Add(new("Events", Count(row.EventCount)));
        if (row.ConfirmedStillRechargingCount > 0) details.Add(new("Still recharging", Count(row.ConfirmedStillRechargingCount)));
        if (row.ConfirmedRechargeCompletedCount > 0) details.Add(new("Recharge completed", Count(row.ConfirmedRechargeCompletedCount)));
        return new HealingPower(row, row.IsOverflow ? "Additional powers" : row.PowerName,
            "Player", direction, icon,
            amount, row.EventCount > 0 ? Count(row.EventCount) : null,
            row.IsOverflow ? "Additional powers grouped." : row.CoverageLimited ? Partial : Note(metric.Availability),
            details, endurance);
    }

    private static IReadOnlyList<OffenseTotal> Totals(IEnumerable<HealingPower> rows) =>
        rows.OrderByDescending(AmountHundredths)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Source.Scope)
            .Select(r => new OffenseTotal(r.Name, r.Amount, Count(r.Source.EventCount),
                r.Source.IsOverflow ? "Additional powers grouped." : r.Direction))
            .ToArray();

    private static bool Visible<T>(Metric<T> metric) where T : struct => metric.Value.HasValue
        && metric.Availability is MetricAvailability.Available or MetricAvailability.Incomplete;
    private static string Amount(CombatScaledAmount value) =>
        (value.Hundredths / (decimal)CombatScaledAmount.Scale).ToString("#,##0.00", CultureInfo.InvariantCulture);
    private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private const string Partial = "Partial — some activity may not have been captured.";
    private static string? Note(MetricAvailability availability) => availability == MetricAvailability.Incomplete ? Partial : null;
}

public sealed record HealingPower(CombatPowerAnalysisRow Source, string Name, string Scope, string Direction, ImageSource? Icon,
    string Amount, string? Events, string? Note, IReadOnlyList<OffenseValue> Details, bool IsEndurance);
