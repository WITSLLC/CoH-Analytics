using System.Globalization;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>
/// Historical Compare presentation over Slice 11 <see cref="ComparisonEngine"/>.
/// Difference (A − B) is engine Right − Left with A as Right and B as Left.
/// </summary>
public sealed partial class HistoricalCompareViewModel : ObservableObject
{
    private readonly ComparisonEngine _engine = new();
    private bool _refreshing;
    private IReadOnlyList<CompareMetricRow> _keyMetrics = [];
    private IReadOnlyList<CompareMetricRow> _damage = [];
    private IReadOnlyList<CompareMetricRow> _healing = [];
    private IReadOnlyList<CompareMetricRow> _damageTaken = [];
    private IReadOnlyList<CompareMetricRow> _powerUsage = [];

    public HistoricalCompareViewModel(
        IHistoricalSegmentReader? reader,
        ICharacterRepository? characters,
        HomecomingAccountDiscoveryService? accounts,
        AccountAnonymityService anonymity)
    {
        SideA = new HistoricalCompareSideViewModel(reader, characters, accounts, anonymity);
        SideB = new HistoricalCompareSideViewModel(reader, characters, accounts, anonymity);
        SideA.PropertyChanged += OnSideChanged;
        SideB.PropertyChanged += OnSideChanged;
        Categories =
        [
            new CompareCategoryChoice(CompareCategoryId.KeyMetrics, "Key Metrics", isActive: true),
            new CompareCategoryChoice(CompareCategoryId.Damage, "Damage"),
            new CompareCategoryChoice(CompareCategoryId.Healing, "Healing"),
            new CompareCategoryChoice(CompareCategoryId.DamageTaken, "Damage Taken"),
            new CompareCategoryChoice(CompareCategoryId.PowerUsage, "Power Usage")
        ];
    }

    public HistoricalCompareSideViewModel SideA { get; }
    public HistoricalCompareSideViewModel SideB { get; }
    public IReadOnlyList<CompareCategoryChoice> Categories { get; }
    public bool IsKeyMetricsSelected => SelectedCategory == CompareCategoryId.KeyMetrics;
    public bool IsDamageSelected => SelectedCategory == CompareCategoryId.Damage;
    public bool IsHealingSelected => SelectedCategory == CompareCategoryId.Healing;
    public bool IsDamageTakenSelected => SelectedCategory == CompareCategoryId.DamageTaken;
    public bool IsPowerUsageSelected => SelectedCategory == CompareCategoryId.PowerUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeyMetricsSelected))]
    [NotifyPropertyChangedFor(nameof(IsDamageSelected))]
    [NotifyPropertyChangedFor(nameof(IsHealingSelected))]
    [NotifyPropertyChangedFor(nameof(IsDamageTakenSelected))]
    [NotifyPropertyChangedFor(nameof(IsPowerUsageSelected))]
    [NotifyPropertyChangedFor(nameof(TableTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentRows))]
    private CompareCategoryId _selectedCategory = CompareCategoryId.KeyMetrics;

    [ObservableProperty] private IReadOnlyList<CompareDifferenceRow> _largestDifferences = [];
    [ObservableProperty] private bool _hasComparison;

    public IReadOnlyList<CompareMetricRow> CurrentRows => SelectedCategory switch
    {
        CompareCategoryId.Damage => _damage,
        CompareCategoryId.Healing => _healing,
        CompareCategoryId.DamageTaken => _damageTaken,
        CompareCategoryId.PowerUsage => _powerUsage,
        _ => _keyMetrics
    };

    public string TableTitle => SelectedCategory switch
    {
        CompareCategoryId.Damage => "Damage Comparison",
        CompareCategoryId.Healing => "Healing Comparison",
        CompareCategoryId.DamageTaken => "Damage Taken Comparison",
        CompareCategoryId.PowerUsage => "Power Usage Comparison",
        _ => "Key Metrics Comparison"
    };

    public string SideAColumnLabel => string.IsNullOrWhiteSpace(SideA.CharacterName)
        || SideA.CharacterName == HistoricalCompareSideViewModel.EmptyName
        ? "Segment A"
        : SideA.CharacterName;

    public string SideBColumnLabel => string.IsNullOrWhiteSpace(SideB.CharacterName)
        || SideB.CharacterName == HistoricalCompareSideViewModel.EmptyName
        ? "Segment B"
        : SideB.CharacterName;

    partial void OnSelectedCategoryChanged(CompareCategoryId value)
    {
        foreach (var category in Categories) category.IsActive = category.Id == value;
        OnPropertyChanged(nameof(CurrentRows));
    }

    [RelayCommand]
    private void SelectCategory(CompareCategoryId category) => SelectedCategory = category;

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            var aSegment = SideA.SelectedSegment?.Header.SegmentId;
            var bSegment = SideB.SelectedSegment?.Header.SegmentId;
            SideA.Refresh();
            SideB.Refresh();
            if (aSegment is null && bSegment is null
                && SideA.SelectedSegment?.Header.SegmentId == SideB.SelectedSegment?.Header.SegmentId)
            {
                var alternate = SideB.SegmentChoices.FirstOrDefault(s =>
                    s.Header.SegmentId != SideA.SelectedSegment?.Header.SegmentId);
                if (alternate is not null) SideB.SelectedSegment = alternate;
            }
        }
        finally { _refreshing = false; }
        Rebuild();
    }

    internal void ApplyProjections(AnalyticalProjectionView? a, AnalyticalProjectionView? b)
    {
        Rebuild(a, b);
    }

    private void OnSideChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_refreshing) return;
        if (e.PropertyName is nameof(HistoricalCompareSideViewModel.ProjectionView)
            or nameof(HistoricalCompareSideViewModel.SelectedAccount)
            or nameof(HistoricalCompareSideViewModel.SelectedCharacter)
            or nameof(HistoricalCompareSideViewModel.SelectedSegment)
            or nameof(HistoricalCompareSideViewModel.CharacterName))
        {
            Rebuild();
        }
    }

    private void Rebuild() => Rebuild(SideA.ProjectionView, SideB.ProjectionView);

    private void Rebuild(AnalyticalProjectionView? a, AnalyticalProjectionView? b)
    {
        OnPropertyChanged(nameof(SideAColumnLabel));
        OnPropertyChanged(nameof(SideBColumnLabel));
        if (a is null || b is null)
        {
            HasComparison = false;
            _keyMetrics = _damage = _healing = _damageTaken = _powerUsage = [];
            LargestDifferences = [];
            OnPropertyChanged(nameof(CurrentRows));
            return;
        }

        // Engine delta is Right − Left. Passing B then A makes Difference (A − B)
        // and percent (A − B) / |B|, matching the Compare table contract.
        var comparison = _engine.Compare(b, a);
        HasComparison = true;
        _keyMetrics = KeyMetrics(comparison);
        _damage = DamageRows(comparison);
        _healing = HealingRows(comparison);
        _damageTaken = DamageTakenRows(comparison);
        _powerUsage = PowerUsageRows(comparison);
        LargestDifferences = RankDifferences(_keyMetrics);
        OnPropertyChanged(nameof(CurrentRows));
    }

    private static IReadOnlyList<CompareMetricRow> KeyMetrics(AnalyticalComparison comparison) =>
    [
        Row("Total Damage", comparison.Session.DamageDealt),
        Row("Player Damage", comparison.Session.DamageDealtSelf),
        Row("Pet Damage", comparison.Session.DamageDealtOwnedPets),
        Row("Proc Damage", comparison.Attribution.ProcDamage),
        Row("Damage Taken", comparison.Session.DamageReceived),
        Row("Healing Dealt", comparison.Session.HealingDealt),
        Row("Endurance Granted", comparison.Session.EnduranceGranted),
        DurationRow("Session Duration", comparison.Clock.WallClockDuration),
        DpsRow("DPS (Total)", comparison.Clock.WallClockDamagePerSecondHundredths)
    ];

    private static IReadOnlyList<CompareMetricRow> DamageRows(AnalyticalComparison comparison)
    {
        var rows = new List<CompareMetricRow>();
        foreach (var power in comparison.Powers)
        {
            if (power.Key.Direction != CombatAnalyticsDirection.Outgoing
                || power.Key.Scope == CombatAnalyticsScope.PerPet)
            {
                continue;
            }

            if (!HasMagnitude(power.DamageMagnitude, power.Right?.DamageMagnitudeMetric, power.Left?.DamageMagnitudeMetric))
            {
                continue;
            }

            rows.Add(Row(PowerLabel(power.Key, "Damage"), power.DamageMagnitude, power.Right?.DamageMagnitudeMetric, power.Left?.DamageMagnitudeMetric));
        }

        foreach (var type in comparison.OutgoingDamageTypes)
        {
            if (type.IsOverflow)
            {
                rows.Add(Row("Additional damage types", type.Amount));
                continue;
            }

            rows.Add(Row(TypeLabel(type.DamageType), type.Amount));
        }

        return rows;
    }

    private static IReadOnlyList<CompareMetricRow> HealingRows(AnalyticalComparison comparison)
    {
        var rows = new List<CompareMetricRow>();
        foreach (var power in comparison.Powers)
        {
            if (power.Key.Direction != CombatAnalyticsDirection.Outgoing
                || power.Key.Scope != CombatAnalyticsScope.Self)
            {
                continue;
            }

            if (HasMagnitude(power.HealingMagnitude, power.Right?.HealingMagnitudeMetric, power.Left?.HealingMagnitudeMetric))
            {
                rows.Add(Row(PowerLabel(power.Key, "Healing"), power.HealingMagnitude,
                    power.Right?.HealingMagnitudeMetric, power.Left?.HealingMagnitudeMetric));
            }

            if (HasMagnitude(power.EnduranceMagnitude, power.Right?.EnduranceMagnitudeMetric, power.Left?.EnduranceMagnitudeMetric))
            {
                rows.Add(Row(PowerLabel(power.Key, "Endurance"), power.EnduranceMagnitude,
                    power.Right?.EnduranceMagnitudeMetric, power.Left?.EnduranceMagnitudeMetric));
            }
        }

        return rows;
    }

    private static IReadOnlyList<CompareMetricRow> DamageTakenRows(AnalyticalComparison comparison)
    {
        var rows = new List<CompareMetricRow>();
        foreach (var power in comparison.Powers)
        {
            if (power.Key.Direction != CombatAnalyticsDirection.Incoming)
            {
                continue;
            }

            if (HasMagnitude(power.DamageMagnitude, power.Right?.DamageMagnitudeMetric, power.Left?.DamageMagnitudeMetric))
            {
                rows.Add(Row(PowerLabel(power.Key, "Incoming"), power.DamageMagnitude,
                    power.Right?.DamageMagnitudeMetric, power.Left?.DamageMagnitudeMetric));
            }

            if (HasMagnitude(power.LargestHit, OptionalAmount(power.Right?.LargestHit), OptionalAmount(power.Left?.LargestHit)))
            {
                rows.Add(Row(PowerLabel(power.Key, "Largest hit"), power.LargestHit,
                    OptionalAmount(power.Right?.LargestHit), OptionalAmount(power.Left?.LargestHit)));
            }
        }

        foreach (var type in comparison.IncomingDamageTypes)
        {
            if (type.IsOverflow)
            {
                rows.Add(Row("Additional incoming damage types", type.Amount));
                continue;
            }

            rows.Add(Row(TypeLabel(type.DamageType) + " taken", type.Amount));
        }

        return rows;
    }

    private static IReadOnlyList<CompareMetricRow> PowerUsageRows(AnalyticalComparison comparison)
    {
        var rows = new List<CompareMetricRow>
        {
            CountRow("Activations", comparison.Session.ActivationCount),
            CountRow("Attack resolutions", comparison.Session.AttackResolutionCount),
            CountRow("Recharge completed", comparison.Session.ConfirmedRechargeCompletedCount),
            CountRow("Still recharging", comparison.Session.ConfirmedStillRechargingCount)
        };
        foreach (var power in comparison.Powers)
        {
            if (power.Key.Direction != CombatAnalyticsDirection.Outgoing
                || power.Key.Scope == CombatAnalyticsScope.PerPet)
            {
                continue;
            }

            if (HasCount(power.ActivationCount, power.Right?.ActivationCount, power.Left?.ActivationCount))
            {
                rows.Add(CountRow(PowerLabel(power.Key, "Activations"), power.ActivationCount,
                    ObservedLong(power.Right?.ActivationCount), ObservedLong(power.Left?.ActivationCount)));
            }

            if (HasCount(power.EventCount, power.Right?.EventCount, power.Left?.EventCount))
            {
                rows.Add(CountRow(PowerLabel(power.Key, "Events"), power.EventCount,
                    ObservedLong(power.Right?.EventCount), ObservedLong(power.Left?.EventCount)));
            }
        }

        return rows;
    }

    private static IReadOnlyList<CompareDifferenceRow> RankDifferences(IReadOnlyList<CompareMetricRow> keyMetrics)
    {
        return keyMetrics
            .Where(row => row.RankValue is { } value && value != 0
                && row.Label is not "Session Duration")
            .OrderByDescending(row => Math.Abs(row.RankValue!.Value))
            .ThenBy(row => row.Label, StringComparer.Ordinal)
            .Take(5)
            .Select((row, index) => new CompareDifferenceRow(
                index + 1,
                row.Label,
                row.RankValue > 0 ? "Segment A recorded more." : "Segment A recorded less.",
                row.Difference,
                row.Trend,
                row.TrendBrushKey,
                row.Arrow))
            .ToArray();
    }

    private static CompareMetricRow Row(
        string label,
        MetricComparison<CombatScaledAmount> comparison,
        Metric<CombatScaledAmount>? fallbackA = null,
        Metric<CombatScaledAmount>? fallbackB = null)
    {
        var left = comparison.Left.Availability is MetricAvailability.Available or MetricAvailability.Incomplete
            ? comparison.Left
            : fallbackB ?? comparison.Left;
        var right = comparison.Right.Availability is MetricAvailability.Available or MetricAvailability.Incomplete
            ? comparison.Right
            : fallbackA ?? comparison.Right;
        var presented = comparison with { Left = left, Right = right };
        var delta = Comparable(presented) ? presented.AbsoluteDelta.Value?.Hundredths : null;
        return new CompareMetricRow(
            label,
            FormatAmount(right),
            FormatAmount(left),
            FormatSignedAmount(presented),
            FormatPercent(presented),
            Trend(delta),
            BrushKey(delta),
            Arrow(delta),
            delta,
            FormatAbsoluteAmount(delta));
    }

    private static CompareMetricRow CountRow(
        string label,
        MetricComparison<long> comparison,
        Metric<long>? fallbackA = null,
        Metric<long>? fallbackB = null)
    {
        var left = comparison.Left.Availability is MetricAvailability.Available or MetricAvailability.Incomplete
            ? comparison.Left
            : fallbackB ?? comparison.Left;
        var right = comparison.Right.Availability is MetricAvailability.Available or MetricAvailability.Incomplete
            ? comparison.Right
            : fallbackA ?? comparison.Right;
        var presented = comparison with { Left = left, Right = right };
        var delta = Comparable(presented) ? presented.AbsoluteDelta.Value : null;
        return new CompareMetricRow(
            label,
            FormatCount(right),
            FormatCount(left),
            FormatSignedCount(presented),
            FormatPercent(presented),
            Trend(delta),
            BrushKey(delta),
            Arrow(delta),
            delta,
            delta is { } value ? Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture) : "—");
    }

    private static CompareMetricRow DurationRow(string label, MetricComparison<TimeSpan> comparison)
    {
        var deltaTicks = Comparable(comparison) ? comparison.AbsoluteDelta.Value?.Ticks : null;
        // Engine still computes percent; elapsed-time percent is not a useful Compare display.
        return new CompareMetricRow(
            label,
            FormatDuration(comparison.Right),
            FormatDuration(comparison.Left),
            FormatSignedDuration(comparison),
            Missing,
            Trend(deltaTicks),
            BrushKey(deltaTicks),
            Arrow(deltaTicks),
            null,
            FormatAbsoluteDuration(comparison.AbsoluteDelta.Value));
    }

    private static CompareMetricRow DpsRow(string label, MetricComparison<long> comparison)
    {
        var delta = Comparable(comparison) ? comparison.AbsoluteDelta.Value : null;
        return new CompareMetricRow(
            label,
            FormatDps(comparison.Right),
            FormatDps(comparison.Left),
            FormatSignedDps(comparison),
            FormatPercent(comparison),
            Trend(delta),
            BrushKey(delta),
            Arrow(delta),
            delta,
            FormatAbsoluteDps(delta));
    }

    private static bool Comparable<T>(MetricComparison<T> comparison) where T : struct =>
        comparison.State is ComparisonState.Comparable or ComparisonState.Partial
        && comparison.AbsoluteDelta.Value.HasValue;

    private static bool HasMagnitude(
        MetricComparison<CombatScaledAmount> comparison,
        Metric<CombatScaledAmount>? right,
        Metric<CombatScaledAmount>? left) =>
        Visible(comparison.Right) || Visible(comparison.Left)
        || right is { } rightValue && Visible(rightValue)
        || left is { } leftValue && Visible(leftValue);

    private static bool HasCount(MetricComparison<long> comparison, long? right, long? left)
    {
        var a = Visible(comparison.Right) ? comparison.Right.Value : right is > 0 ? right : null;
        var b = Visible(comparison.Left) ? comparison.Left.Value : left is > 0 ? left : null;
        return (a ?? 0) > 0 || (b ?? 0) > 0
            || a is 0 && b is null
            || b is 0 && a is null;
    }

    private static bool Visible<T>(Metric<T> metric) where T : struct =>
        metric.Value.HasValue
        && metric.Availability is MetricAvailability.Available or MetricAvailability.Incomplete;

    private static string PowerLabel(ComparisonPowerKey key, string kind)
    {
        var name = key.IsOverflow ? "Additional powers" : key.PowerName;
        var scope = key.Scope == CombatAnalyticsScope.Self ? "Player" : "Owned pets";
        return $"{name} ({scope}) · {kind}";
    }

    private static string TypeLabel(DamageType type) =>
        (type.IsUnresistable ? "Unresistable " : "") + type.Text;

    private static Metric<CombatScaledAmount> OptionalAmount(CombatScaledAmount? amount) =>
        amount is { } value ? Metric<CombatScaledAmount>.Available(value) : Metric<CombatScaledAmount>.NotCaptured();

    private static Metric<long> ObservedLong(long? value) =>
        value is > 0 ? Metric<long>.Available(value.Value) : Metric<long>.NotCaptured();

    private static string Missing => "—";

    private static string FormatAmount(Metric<CombatScaledAmount> metric) =>
        Visible(metric) ? Amount(metric.Value!.Value) : Missing;

    private static string FormatCount(Metric<long> metric) =>
        Visible(metric) ? metric.Value!.Value.ToString("N0", CultureInfo.InvariantCulture) : Missing;

    private static string FormatDps(Metric<long> metric) =>
        Visible(metric) ? Amount(new CombatScaledAmount(metric.Value!.Value)) : Missing;

    private static string FormatDuration(Metric<TimeSpan> metric) =>
        Visible(metric) ? Clock(metric.Value!.Value) : Missing;

    private static string FormatSignedAmount(MetricComparison<CombatScaledAmount> comparison)
    {
        if (!Comparable(comparison)) return Missing;
        var hundredths = comparison.AbsoluteDelta.Value!.Value.Hundredths;
        return LabeledHigher(hundredths, Amount(new CombatScaledAmount(Math.Abs(hundredths))));
    }

    private static string FormatSignedCount(MetricComparison<long> comparison)
    {
        if (!Comparable(comparison)) return Missing;
        var value = comparison.AbsoluteDelta.Value!.Value;
        return LabeledHigher(value, Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture));
    }

    private static string FormatSignedDps(MetricComparison<long> comparison)
    {
        if (!Comparable(comparison)) return Missing;
        var value = comparison.AbsoluteDelta.Value!.Value;
        return LabeledHigher(value, Amount(new CombatScaledAmount(Math.Abs(value))));
    }

    private static string FormatSignedDuration(MetricComparison<TimeSpan> comparison)
    {
        if (!Comparable(comparison)) return Missing;
        var value = comparison.AbsoluteDelta.Value!.Value;
        return LabeledHigher(value.Ticks, Clock(value.Duration()));
    }

    private static string FormatPercent<T>(MetricComparison<T> comparison) where T : struct
    {
        if (!Comparable(comparison)
            || comparison.PercentDeltaReason is ComparisonReason.ZeroBaseline
                or ComparisonReason.ArithmeticOverflow
                or ComparisonReason.NotCaptured
                or ComparisonReason.Unsupported
                or ComparisonReason.DenominatorMismatch)
        {
            return Missing;
        }

        if (comparison.PercentDeltaHundredths.Value is not { } hundredths) return Missing;
        var formatted = (Math.Abs(hundredths) / 10000m).ToString("0.0%", CultureInfo.InvariantCulture);
        return LabeledHigher(hundredths, formatted);
    }

    private static string FormatAbsoluteAmount(long? hundredths) =>
        hundredths is { } value ? Amount(new CombatScaledAmount(Math.Abs(value))) : Missing;

    private static string FormatAbsoluteDps(long? hundredths) =>
        hundredths is { } value ? Amount(new CombatScaledAmount(Math.Abs(value))) : Missing;

    private static string FormatAbsoluteDuration(TimeSpan? value) =>
        value is { } duration ? Clock(duration.Duration()) : Missing;

    private static string Amount(CombatScaledAmount value) =>
        (value.Hundredths / (decimal)CombatScaledAmount.Scale).ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Clock(TimeSpan value)
    {
        var duration = value.Duration();
        return duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Engine AbsoluteDelta is A − B. Presentation names the numerically higher side
    /// without changing engine math or implying better/worse.
    /// </summary>
    private static string LabeledHigher(long signedScalar, string magnitude) =>
        signedScalar > 0 ? "A +" + magnitude
        : signedScalar < 0 ? "B +" + magnitude
        : "Equal";

    private static CompareTrend Trend(long? delta) =>
        delta > 0 ? CompareTrend.Higher : delta < 0 ? CompareTrend.Lower : CompareTrend.None;

    private static string BrushKey(long? delta) =>
        delta > 0 ? "Brush.Accent"
        : delta < 0 ? "Brush.InspirationResistance"
        : delta is 0 ? "Brush.TextPrimary"
        : "Brush.TextMuted";

    private static string Arrow(long? delta) =>
        delta > 0 ? "↑" : delta < 0 ? "↓" : "";
}

public sealed partial class HistoricalCompareSideViewModel : ObservableObject
{
    internal const string EmptyName = "Select a historical segment";
    private readonly IHistoricalSegmentReader? _reader;
    private readonly ICharacterRepository? _characters;
    private readonly HomecomingAccountDiscoveryService? _accounts;
    private readonly AccountAnonymityService _anonymity;
    private IReadOnlyList<HistoricalSegmentHeader> _headers = [];
    private IReadOnlyList<CombatCharacterChoice> _allCharacters = [];
    private bool _refreshing;

    public HistoricalCompareSideViewModel(
        IHistoricalSegmentReader? reader,
        ICharacterRepository? characters,
        HomecomingAccountDiscoveryService? accounts,
        AccountAnonymityService anonymity)
    {
        _reader = reader;
        _characters = characters;
        _accounts = accounts;
        _anonymity = anonymity;
    }

    [ObservableProperty] private IReadOnlyList<CombatAccountChoice> _accountsChoices = [];
    [ObservableProperty] private IReadOnlyList<CombatCharacterChoice> _characterChoices = [];
    [ObservableProperty] private IReadOnlyList<CombatSegmentChoice> _segmentChoices = [];
    [ObservableProperty] private CombatAccountChoice? _selectedAccount;
    [ObservableProperty] private CombatCharacterChoice? _selectedCharacter;
    [ObservableProperty] private CombatSegmentChoice? _selectedSegment;
    [ObservableProperty] private AnalyticalProjectionView? _projectionView;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _characterName = EmptyName;
    [ObservableProperty] private string? _accountLabel;
    [ObservableProperty] private string? _characterDetails;
    [ObservableProperty] private string? _sessionLabel;
    [ObservableProperty] private string? _durationLabel;

    public void Refresh()
    {
        var accountId = SelectedAccount?.Id;
        var characterId = SelectedCharacter?.Id;
        var segmentId = SelectedSegment?.Header.SegmentId;
        ErrorMessage = null;
        _refreshing = true;
        try
        {
            _headers = (_reader?.ListHeaders() ?? [])
                .OrderByDescending(h => h.CaptureEndUtc)
                .ThenByDescending(h => h.FinalizedAtUtc ?? h.CaptureEndUtc)
                .ThenBy(h => h.SegmentId, StringComparer.Ordinal).ToArray();
            var choices = (_characters?.Current.Records ?? [])
                .Select(c => new CombatCharacterChoice(c.RecordId, c.AccountStableId, c.CurrentDisplayName))
                .ToDictionary(c => c.Id);
            foreach (var header in _headers)
            {
                var id = CharacterId(header);
                if (id is not null && !choices.ContainsKey(id) && !string.IsNullOrWhiteSpace(header.AccountStableId))
                    choices[id] = new CombatCharacterChoice(id, header.AccountStableId,
                        header.CharacterDisplayNameAtCapture ?? "Unknown character");
            }
            _allCharacters = choices.Values.OrderBy(c => c.Label, StringComparer.CurrentCulture)
                .ThenBy(c => c.Id.ToString(), StringComparer.Ordinal).ToArray();
            AccountsChoices = _allCharacters.Select(c => c.AccountId).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).Select(id => new CombatAccountChoice(id, AccountLabelFor(id))).ToArray();
            var latestCharacter = _headers.Select(h => _allCharacters.FirstOrDefault(c => c.Id == CharacterId(h)))
                .FirstOrDefault(c => c is not null);
            SelectedAccount = AccountsChoices.FirstOrDefault(a => a.Id == accountId)
                ?? AccountsChoices.FirstOrDefault(a => a.Id == latestCharacter?.AccountId)
                ?? AccountsChoices.FirstOrDefault();
            SetCharacters(characterId ?? latestCharacter?.Id);
            SetSegments(segmentId);
        }
        catch (Exception)
        {
            AccountsChoices = [];
            CharacterChoices = [];
            SegmentChoices = [];
            SelectedAccount = null;
            SelectedCharacter = null;
            SelectedSegment = null;
            LoadSelection();
            ErrorMessage = "Historical segments could not be refreshed. Please try again.";
        }
        finally { _refreshing = false; }
    }

    private static CharacterRecordId? CharacterId(HistoricalSegmentHeader h) =>
        h.CanonicalCharacterRecordId ?? h.CharacterRecordId;

    private string AccountLabelFor(string id) => _anonymity.MaskForPresentation(
        _accounts?.Accounts.FirstOrDefault(a => a.StableId == id)?.DisplayName
        ?? _headers.FirstOrDefault(h => h.AccountStableId == id && !string.IsNullOrWhiteSpace(h.AccountDisplayNameAtCapture))?.AccountDisplayNameAtCapture
        ?? id);

    partial void OnSelectedAccountChanged(CombatAccountChoice? value)
    {
        if (_refreshing) return;
        if (value is not null && !AccountsChoices.Contains(value)) { SelectedAccount = null; return; }
        SetCharacters(null);
    }

    private void SetCharacters(CharacterRecordId? preferred)
    {
        CharacterChoices = _allCharacters.Where(c => c.AccountId == SelectedAccount?.Id).ToArray();
        var latest = _headers.Select(h => CharacterChoices.FirstOrDefault(c => c.Id == CharacterId(h)))
            .FirstOrDefault(c => c is not null);
        SelectedCharacter = CharacterChoices.FirstOrDefault(c => c.Id == preferred) ?? latest ?? CharacterChoices.FirstOrDefault();
        if (!_refreshing && SelectedCharacter is null) SetSegments(null);
    }

    partial void OnSelectedCharacterChanged(CombatCharacterChoice? value)
    {
        if (_refreshing) return;
        if (value is not null && !CharacterChoices.Contains(value)) { SelectedCharacter = null; return; }
        SetSegments(null);
    }

    private void SetSegments(string? preferred)
    {
        SegmentChoices = _headers.Where(h => SelectedCharacter is not null && CharacterId(h) == SelectedCharacter.Id)
            .Select(h => new CombatSegmentChoice(h, ReadRunName(h))).ToArray();
        SelectedSegment = SegmentChoices.FirstOrDefault(s => s.Header.SegmentId == preferred) ?? SegmentChoices.FirstOrDefault();
        if (_refreshing || SelectedSegment is null) LoadSelection();
    }

    private string? ReadRunName(HistoricalSegmentHeader header)
    {
        if (header.CaptureKind != HistoricalCaptureKind.DurableSegment) return null;
        try
        {
            return _reader?.TryLoad(header.SegmentId,
                new HistoricalLoadOptions { IncludeManifest = false }).Segment?.Annotations?.UserDisplayName;
        }
        catch (Exception) { return null; }
    }

    partial void OnSelectedSegmentChanged(CombatSegmentChoice? value)
    {
        if (_refreshing) return;
        if (value is not null && !SegmentChoices.Contains(value)) { SelectedSegment = null; return; }
        LoadSelection();
    }

    private void LoadSelection()
    {
        ErrorMessage = null;
        ProjectionView = null;
        CharacterName = EmptyName;
        AccountLabel = CharacterDetails = SessionLabel = DurationLabel = null;
        try
        {
            if (SelectedSegment is not { } choice) return;
            var result = _reader?.TryLoad(choice.Header.SegmentId);
            if (result?.HasAuthoritativeAggregates != true || result.Segment is not { } segment)
            {
                ErrorMessage = "This segment has no readable historical analytics.";
                return;
            }

            ProjectionView = segment.TryAsProjectionView();
            var h = segment.Header;
            CharacterName = h.CharacterDisplayNameAtCapture ?? SelectedCharacter?.Label ?? "Unknown character";
            AccountLabel = SelectedAccount?.Label;
            var details = new List<string>();
            if (h.LevelAtCapture is { } level) details.Add($"Level {level}");
            if (!string.IsNullOrWhiteSpace(h.Archetype)) details.Add(h.Archetype);
            var powersets = new[] { h.PrimaryPowerSet, h.SecondaryPowerSet }.Where(p => !string.IsNullOrWhiteSpace(p));
            if (powersets.Any()) details.Add(string.Join(" / ", powersets));
            CharacterDetails = details.Count == 0 ? null : string.Join("  ·  ", details);
            SessionLabel = string.IsNullOrWhiteSpace(choice.RunName)
                ? HistoricalCombatViewModel.FormatDate(h.CaptureStartUtc)
                : $"{choice.RunName} — {HistoricalCombatViewModel.FormatDate(h.CaptureStartUtc)}";
            var duration = segment.Aggregates!.Clock.WallClockDuration;
            if (duration.Availability == MetricAvailability.Available && duration.Value is { } value)
                DurationLabel = value.ToString(value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.CurrentCulture);
        }
        catch (Exception) { ErrorMessage = "The historical segment could not be loaded. Please try again."; }
    }
}

public enum CompareCategoryId { KeyMetrics, Damage, Healing, DamageTaken, PowerUsage }

public enum CompareTrend { None, Higher, Lower }

public sealed partial class CompareCategoryChoice(CompareCategoryId id, string label, bool isActive = false) : ObservableObject
{
    public CompareCategoryId Id { get; } = id;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isActive = isActive;
}

public sealed record CompareMetricRow(
    string Label,
    string SideA,
    string SideB,
    string Difference,
    string Percent,
    CompareTrend Trend,
    string TrendBrushKey,
    string Arrow,
    long? RankValue,
    string AbsoluteDisplay);

public sealed record CompareDifferenceRow(
    int Rank,
    string Label,
    string Detail,
    string Difference,
    CompareTrend Trend,
    string TrendBrushKey,
    string Arrow);
