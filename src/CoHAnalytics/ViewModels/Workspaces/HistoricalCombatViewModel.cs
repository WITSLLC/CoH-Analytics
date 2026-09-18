using System.Globalization;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>
/// Historical selection and context only. Future combat panels consume ProjectionView,
/// without depending on this browser or its persistence services.
/// </summary>
public sealed partial class HistoricalCombatViewModel : ObservableObject
{
    private readonly IHistoricalSegmentReader? _reader;
    private readonly ICharacterRepository? _characters;
    private readonly HomecomingAccountDiscoveryService? _accounts;
    private readonly AccountAnonymityService _anonymity;
    private readonly ISegmentAnnotationWriter? _annotations;
    private readonly ISegmentReportService? _reports;
    private IReadOnlyList<HistoricalSegmentHeader> _headers = [];
    private IReadOnlyList<CombatCharacterChoice> _allCharacters = [];
    private bool _refreshing;
    private HistoricalSegment? _loaded;

    public HistoricalCombatViewModel(
        IHistoricalSegmentReader? reader,
        ICharacterRepository? characters,
        HomecomingAccountDiscoveryService? accounts,
        AccountAnonymityService anonymity,
        ISegmentAnnotationWriter? annotations,
        ISegmentReportService? reports,
        IHomecomingPowerReferenceCatalog? powerCatalog = null,
        IInstalledGameAssetProvider? assets = null,
        IItemReferenceCatalog? items = null,
        IEnhancementIconCompositor? compositor = null,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        _reader = reader;
        _characters = characters;
        _accounts = accounts;
        _anonymity = anonymity;
        _annotations = annotations;
        _reports = reports;
        Offense = new CombatOffenseViewModel(powerCatalog, assets, items, compositor, boostMetadata);
        Incoming = new CombatIncomingViewModel(powerCatalog, assets, items, compositor, boostMetadata);
    }

    [ObservableProperty] private IReadOnlyList<CombatAccountChoice> _accountsChoices = [];
    [ObservableProperty] private IReadOnlyList<CombatCharacterChoice> _characterChoices = [];
    [ObservableProperty] private IReadOnlyList<CombatSegmentChoice> _segmentChoices = [];
    [ObservableProperty] private CombatAccountChoice? _selectedAccount;
    [ObservableProperty] private CombatCharacterChoice? _selectedCharacter;
    [ObservableProperty] private CombatSegmentChoice? _selectedSegment;
    [ObservableProperty] private AnalyticalProjectionView? _projectionView;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _characterName = "Select a historical segment";
    [ObservableProperty] private string? _characterDetails;
    [ObservableProperty] private string? _captureStart;
    [ObservableProperty] private string? _captureEnd;
    [ObservableProperty] private string? _sessionLength;
    [ObservableProperty] private string? _buildLabel;
    [ObservableProperty] private string? _runNameHint;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRunNameCommand))]
    private string _runName = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRunNameCommand))]
    private bool _canEditRunName;

    public CombatOffenseViewModel Offense { get; }
    public CombatIncomingViewModel Incoming { get; }
    public bool IsOffenseSelected => SelectedSection == CombatSectionId.Offense;
    public bool IsIncomingSelected => SelectedSection == CombatSectionId.Incoming;
    public bool IsOtherSectionSelected => !IsOffenseSelected && !IsIncomingSelected;

    public IReadOnlyList<CombatSectionChoice> Sections { get; } =
        Enum.GetValues<CombatSectionId>().Select(id => new CombatSectionChoice(id)).ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffenseSelected))]
    [NotifyPropertyChangedFor(nameof(IsIncomingSelected))]
    [NotifyPropertyChangedFor(nameof(IsOtherSectionSelected))]
    private CombatSectionId _selectedSection = CombatSectionId.Offense;

    partial void OnSelectedSectionChanged(CombatSectionId value)
    {
        foreach (var section in Sections) section.IsActive = section.Id == value;
    }

    [RelayCommand]
    private void SelectSection(CombatSectionId section) => SelectedSection = section;

    /// <summary>Refresh on entry or user request, never on the live combat timer.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var accountId = SelectedAccount?.Id;
        var characterId = SelectedCharacter?.Id;
        var segmentId = SelectedSegment?.Header.SegmentId;
        var draftName = CanSaveRunName() ? RunName : null;
        ErrorMessage = null;
        _refreshing = true;
        try
        {
            _headers = (_reader?.ListHeaders() ?? [])
                .OrderByDescending(h => h.CaptureEndUtc)
                .ThenByDescending(h => h.FinalizedAtUtc ?? h.CaptureEndUtc)
                .ThenBy(h => h.SegmentId, StringComparer.Ordinal).ToArray();

            // Trusted records establish ownership for legacy observations; capture headers
            // also retain ownership when a current character record is no longer present.
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
                .Order(StringComparer.Ordinal).Select(id => new CombatAccountChoice(id, AccountLabel(id))).ToArray();
            var latestCharacter = _headers.Select(h => _allCharacters.FirstOrDefault(c => c.Id == CharacterId(h)))
                .FirstOrDefault(c => c is not null);
            SelectedAccount = AccountsChoices.FirstOrDefault(a => a.Id == accountId)
                ?? AccountsChoices.FirstOrDefault(a => a.Id == latestCharacter?.AccountId)
                ?? AccountsChoices.FirstOrDefault();
            SetCharacters(characterId ?? latestCharacter?.Id);
            SetSegments(segmentId);
            if (draftName is not null && SelectedSegment?.Header.SegmentId == segmentId && CanEditRunName)
                RunName = draftName;
            if (_headers.Any(h => !_allCharacters.Any(c => c.Id == CharacterId(h))))
                ErrorMessage = "Some captures have no stored account/character association and cannot be listed here.";
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

    private string AccountLabel(string id) => _anonymity.MaskForPresentation(
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
        // Refresh suppresses property callbacks; also clear stale context for empty lists.
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
        catch (Exception) { return null; } // A bad sidecar must not hide a capture.
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
        _loaded = null;
        ProjectionView = null;
            Offense.SetProjection(null);
            Incoming.SetProjection(null);
        CanEditRunName = false;
        RunName = string.Empty;
        CharacterName = "Select a historical segment";
        CharacterDetails = CaptureStart = CaptureEnd = SessionLength = BuildLabel = RunNameHint = null;
        try
        {
            if (SelectedSegment is not { } choice) return;
            var result = _reader?.TryLoad(choice.Header.SegmentId);
            if (result?.HasAuthoritativeAggregates != true || result.Segment is not { } segment)
            {
                ErrorMessage = "This segment has no readable historical analytics.";
                return;
            }
            _loaded = segment;
            ProjectionView = segment.TryAsProjectionView();
            Offense.SetProjection(ProjectionView?.Projection, segment.FrozenManifest);
            Incoming.SetProjection(ProjectionView?.Projection, segment.FrozenManifest);
            var h = segment.Header;
            CharacterName = h.CharacterDisplayNameAtCapture ?? SelectedCharacter?.Label ?? "Unknown character";
            var details = new List<string>();
            if (h.LevelAtCapture is { } level) details.Add($"Level {level}");
            if (!string.IsNullOrWhiteSpace(h.Archetype)) details.Add(h.Archetype);
            var powersets = new[] { h.PrimaryPowerSet, h.SecondaryPowerSet }.Where(p => !string.IsNullOrWhiteSpace(p));
            if (powersets.Any()) details.Add(string.Join(" / ", powersets));
            CharacterDetails = details.Count == 0 ? null : string.Join("  |  ", details);
            CaptureStart = FormatDate(h.CaptureStartUtc);
            CaptureEnd = FormatDate(h.CaptureEndUtc);
            var duration = segment.Aggregates!.Clock.WallClockDuration;
            if (duration.Availability == MetricAvailability.Available && duration.Value is { } value)
                SessionLength = value.ToString(value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.CurrentCulture);
            if (segment.BuildContextStatus == HistoricalBuildContextStatus.Present)
                BuildLabel = "Build captured for this session";
            RunName = segment.Annotations?.UserDisplayName ?? string.Empty;
            CanEditRunName = h.CaptureKind == HistoricalCaptureKind.DurableSegment && _annotations is not null
                && segment.AnnotationStatus is HistoricalAnnotationStatus.Present or HistoricalAnnotationStatus.Missing;
            if (h.CaptureKind == HistoricalCaptureKind.LegacyObservation)
                RunNameHint = "Run Name editing is available for durable segments only.";
            else if (!CanEditRunName)
                RunNameHint = "Run Name editing is unavailable for this segment.";
        }
        catch (Exception) { ErrorMessage = "The historical segment could not be loaded. Please try again."; }
        finally
        {
            ReportCommand.NotifyCanExecuteChanged();
            SaveRunNameCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSaveRunName() => CanEditRunName && _loaded is not null
        && !string.Equals(RunName.Trim(), _loaded.Annotations?.UserDisplayName ?? string.Empty, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanSaveRunName))]
    private void SaveRunName()
    {
        if (!CanSaveRunName() || _loaded is null) return;
        try
        {
            // Read again so renaming preserves other current annotations (notes, exclusion, beta).
            var current = _reader?.TryLoad(_loaded.Header.SegmentId).Segment;
            if (current?.Header.CaptureKind != HistoricalCaptureKind.DurableSegment
                || current.AnnotationStatus is not (HistoricalAnnotationStatus.Present or HistoricalAnnotationStatus.Missing))
            {
                ErrorMessage = "The Run Name could not be saved because the segment annotations are unavailable.";
                return;
            }
            var name = RunName.Trim();
            var result = _annotations!.TryUpdateAnnotations(current.Header.GameplaySessionId,
                current.Header.SegmentOrdinal, (current.Annotations ?? new SegmentAnnotations()) with
                { UserDisplayName = name.Length == 0 ? null : name });
            if (!result.IsSuccess) { ErrorMessage = "The Run Name could not be saved. Please try again."; return; }
            _loaded = current with { Annotations = (current.Annotations ?? new SegmentAnnotations()) with { UserDisplayName = name } };
            RunName = name;
            Refresh();
        }
        catch (Exception) { ErrorMessage = "The Run Name could not be saved. Please try again."; }
    }

    private bool CanReport() => _reports is not null && _loaded is not null && ProjectionView is not null
        && SelectedSegment?.Header.SegmentId == _loaded.Header.SegmentId;

    [RelayCommand(CanExecute = nameof(CanReport))]
    private void Report()
    {
        if (!CanReport()) return;
        try { ErrorMessage = _reports!.GenerateAndOpen(_loaded!.Header.GameplaySessionId, _loaded.Header.SegmentOrdinal); }
        catch (Exception) { ErrorMessage = "The Segment report could not be generated. Please try again."; }
    }

    internal static string FormatDate(DateTimeOffset value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}

public sealed record CombatAccountChoice(string Id, string Label);
public sealed record CombatCharacterChoice(CharacterRecordId Id, string AccountId, string Label);
public sealed record CombatSegmentChoice(HistoricalSegmentHeader Header, string? RunName)
{
    public string Label => string.IsNullOrWhiteSpace(RunName) ? HistoricalCombatViewModel.FormatDate(Header.CaptureStartUtc)
        : $"{RunName} — {HistoricalCombatViewModel.FormatDate(Header.CaptureStartUtc)}";
}

public enum CombatSectionId { Offense, Incoming, Healing, Pets }

public sealed partial class CombatSectionChoice(CombatSectionId id) : ObservableObject
{
    public CombatSectionId Id { get; } = id;
    public string Label => Id.ToString();
    [ObservableProperty] private bool _isActive = id == CombatSectionId.Offense;
}
