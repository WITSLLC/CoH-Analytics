using System.Collections.ObjectModel;
using CoHAnalytics.Models;
using CoHAnalytics.Navigation;
using CoHAnalytics.Observations;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>
/// Gated internal tooling workspace. Display title comes from presentation metadata
/// and is not the architectural identity of this workspace.
/// </summary>
public sealed partial class InternalToolsViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private readonly AccountAnonymityService _accountAnonymityService;
    private readonly IAcquisitionObservationService _observationService;
    private readonly IItemReferenceCatalog _itemReferenceCatalog;
    private readonly AcquisitionClassificationService _classificationService;
    private bool _classificationCommandInProgress;
    private bool _queueRefreshPending;
    private bool _disposed;

    public InternalToolsViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        AccountAnonymityService accountAnonymityService,
        IAcquisitionObservationService observationService,
        IItemReferenceCatalog itemReferenceCatalog,
        AcquisitionClassificationService classificationService)
        : base(orchestrator, gameRuntimeService)
    {
        _accountAnonymityService = accountAnonymityService;
        _observationService = observationService;
        _itemReferenceCatalog = itemReferenceCatalog;
        _classificationService = classificationService;
        _selectedFamilyFilter = FamilyFilters[0];
        _accountAnonymityEnabled = accountAnonymityService.IsEnabled;
        _accountAnonymityService.Changed += OnAccountAnonymityChanged;
        _observationService.Changed += OnObservationChanged;
        WireEnvironmentStatus();
        RefreshObservationQueue();
    }

    public override string Title => InternalToolsWorkspacePresentation.Title;

    public string Description => InternalToolsWorkspacePresentation.Description;

    public ObservableCollection<AcquisitionObservationRowViewModel> NeedsClassification { get; } = [];

    public ObservableCollection<AcquisitionEvidenceRowViewModel> SelectedEvidence { get; } = [];

    public ObservableCollection<ItemReferenceSearchRowViewModel> ReferenceSearchResults { get; } = [];

    public ObservableCollection<ItemReferenceSearchRowViewModel> ProducedEnhancementSearchResults { get; } = [];

    public bool IsAuthoringAvailable => _classificationService.IsAuthoringAvailable;

    public string AuthoringAvailabilityDetail => _classificationService.AuthoringAvailabilityDetail;

    public IReadOnlyList<ObservationFamilyFilterOption> FamilyFilters { get; } =
    [
        new(null, "All families"),
        new(ReferenceItemFamily.Recipe, "Recipe"),
        new(ReferenceItemFamily.Enhancement, "Enhancement"),
        new(ReferenceItemFamily.Salvage, "Salvage"),
        new(ReferenceItemFamily.Inspiration, "Inspiration")
    ];

    [ObservableProperty]
    private string _observationSearchText = string.Empty;

    [ObservableProperty]
    private ObservationFamilyFilterOption _selectedFamilyFilter = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedObservation))]
    [NotifyPropertyChangedFor(nameof(CanCreateEnhancementForSelection))]
    [NotifyCanExecuteChangedFor(nameof(MapAndNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateEnhancementAndNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateRecipeAndNextCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreateRecipeForSelection))]
    private AcquisitionObservationRowViewModel? _selectedObservation;

    [ObservableProperty]
    private string _selectedFirstSeenLabel = "—";

    [ObservableProperty]
    private string _selectedLastSeenLabel = "—";

    [ObservableProperty]
    private string _selectedFailedVersionsLabel = "—";

    [ObservableProperty]
    private string _selectedDeveloperNotes = string.Empty;

    [ObservableProperty]
    private string _referenceSearchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MapAndNextCommand))]
    private ItemReferenceSearchRowViewModel? _selectedReference;

    [ObservableProperty]
    private string _classificationStatusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateEnhancementAndNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateRecipeAndNextCommand))]
    private string _newReferenceDisplayName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateEnhancementAndNextCommand))]
    private string _newEnhancementSubtype = "SetIO";

    [ObservableProperty]
    private string _newEnhancementSetId = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateEnhancementAndNextCommand))]
    private string _newEnhancementVariant = "Regular";

    [ObservableProperty]
    private string _producedEnhancementSearchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRecipeAndNextCommand))]
    private ItemReferenceSearchRowViewModel? _selectedProducedEnhancement;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRecipeAndNextCommand))]
    private string _newRecipeSubtype = "SetIO";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRecipeAndNextCommand))]
    private string _newRecipeRarity = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsObservationQueueEmpty))]
    private bool _hasObservationQueueItems;

    public bool IsObservationQueueEmpty => !HasObservationQueueItems;

    public bool HasSelectedObservation => SelectedObservation is not null;

    public bool CanCreateEnhancementForSelection =>
        SelectedObservation is not null
        && (SelectedObservation.FamilyHint is null
            || SelectedObservation.FamilyHint is ReferenceItemFamily.Enhancement);

    public bool CanCreateRecipeForSelection =>
        SelectedObservation?.FamilyHint is ReferenceItemFamily.Recipe;

    [ObservableProperty]
    private bool _accountAnonymityEnabled;

    partial void OnAccountAnonymityEnabledChanged(bool value) =>
        _accountAnonymityService.SetEnabled(value);

    partial void OnObservationSearchTextChanged(string value) => RefreshObservationQueue();

    partial void OnReferenceSearchTextChanged(string value) => RefreshReferenceSearch();

    partial void OnProducedEnhancementSearchTextChanged(string value) =>
        RefreshProducedEnhancementSearch();

    partial void OnSelectedFamilyFilterChanged(ObservationFamilyFilterOption value) =>
        RefreshObservationQueue();

    partial void OnSelectedObservationChanged(AcquisitionObservationRowViewModel? value) =>
        ApplySelectedObservation(value);

    private bool CanMapAndNext() =>
        IsAuthoringAvailable && SelectedObservation is not null && SelectedReference is not null;

    [RelayCommand(CanExecute = nameof(CanMapAndNext))]
    private void MapAndNext()
    {
        if (SelectedObservation is null || SelectedReference is null)
        {
            return;
        }

        ExecuteClassificationAndAdvance(() => _classificationService.MapToExistingReference(
            SelectedObservation.NormalizedKey,
            SelectedReference.CatalogItemId));
    }

    private bool CanCreateEnhancementAndNext() =>
        IsAuthoringAvailable
        && SelectedObservation is not null
        && (SelectedObservation.FamilyHint is null
            || SelectedObservation.FamilyHint is ReferenceItemFamily.Enhancement)
        && !string.IsNullOrWhiteSpace(NewReferenceDisplayName)
        && !string.IsNullOrWhiteSpace(NewEnhancementSubtype)
        && !string.IsNullOrWhiteSpace(NewEnhancementVariant);

    [RelayCommand(CanExecute = nameof(CanCreateEnhancementAndNext))]
    private void CreateEnhancementAndNext()
    {
        if (SelectedObservation is null)
        {
            return;
        }

        ExecuteClassificationAndAdvance(() => _classificationService.CreateEnhancementReference(
            SelectedObservation.NormalizedKey,
            new NewEnhancementReference
            {
                CurrentDisplayName = NewReferenceDisplayName,
                Subtype = NewEnhancementSubtype,
                EnhancementSetId = string.IsNullOrWhiteSpace(NewEnhancementSetId)
                    ? null
                    : NewEnhancementSetId,
                Variant = NewEnhancementVariant
            }));
    }

    private bool CanCreateRecipeAndNext() =>
        IsAuthoringAvailable
        && SelectedObservation is not null
        && SelectedObservation.FamilyHint is ReferenceItemFamily.Recipe
        && SelectedProducedEnhancement is not null
        && !string.IsNullOrWhiteSpace(NewReferenceDisplayName)
        && !string.IsNullOrWhiteSpace(NewRecipeSubtype)
        && !string.IsNullOrWhiteSpace(NewRecipeRarity);

    [RelayCommand(CanExecute = nameof(CanCreateRecipeAndNext))]
    private void CreateRecipeAndNext()
    {
        if (SelectedObservation is null || SelectedProducedEnhancement is null)
        {
            return;
        }

        ExecuteClassificationAndAdvance(() => _classificationService.CreateRecipeReference(
            SelectedObservation.NormalizedKey,
            new NewRecipeReference
            {
                CurrentDisplayName = NewReferenceDisplayName,
                Subtype = NewRecipeSubtype,
                ProducedItemId = SelectedProducedEnhancement.CatalogItemId,
                EnhancementSetId = SelectedProducedEnhancement.EnhancementSetId,
                Rarity = NewRecipeRarity
            }));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _accountAnonymityService.Changed -= OnAccountAnonymityChanged;
        _observationService.Changed -= OnObservationChanged;
        UnwireEnvironmentStatus();
    }

    private void OnAccountAnonymityChanged(object? sender, EventArgs e)
    {
        AccountAnonymityEnabled = _accountAnonymityService.IsEnabled;
    }

    private void OnObservationChanged(object? sender, EventArgs e)
    {
        if (_classificationCommandInProgress)
        {
            _queueRefreshPending = true;
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => RefreshObservationQueue());
            return;
        }

        RefreshObservationQueue();
    }

    private void RefreshObservationQueue(int? preferredIndex = null)
    {
        var selectedKey = SelectedObservation?.NormalizedKey;
        var filter = new AcquisitionObservationFilter
        {
            SearchText = ObservationSearchText,
            Family = SelectedFamilyFilter?.Family
        };
        var rows = _observationService.GetNeedsClassification(filter)
            .Select(item => new AcquisitionObservationRowViewModel(item))
            .ToArray();

        NeedsClassification.Clear();
        foreach (var row in rows)
        {
            NeedsClassification.Add(row);
        }

        HasObservationQueueItems = NeedsClassification.Count > 0;
        SelectedObservation = preferredIndex is not null && NeedsClassification.Count > 0
            ? NeedsClassification[Math.Min(preferredIndex.Value, NeedsClassification.Count - 1)]
            : NeedsClassification.FirstOrDefault(row =>
                string.Equals(row.NormalizedKey, selectedKey, StringComparison.Ordinal))
                ?? NeedsClassification.FirstOrDefault();
    }

    private void ApplySelectedObservation(AcquisitionObservationRowViewModel? selected)
    {
        RefreshSelectedObservationDetail(selected);
        ClassificationStatusMessage = string.Empty;
        SelectedReference = null;
        ReferenceSearchText = selected?.ObservedText ?? string.Empty;
        NewReferenceDisplayName = selected is null
            ? string.Empty
            : StripRecipeSuffix(selected.ObservedText);
        ProducedEnhancementSearchText = selected is not null
            && selected.FamilyHint is ReferenceItemFamily.Recipe
                ? StripRecipeSuffix(selected.ObservedText)
                : string.Empty;
        MapAndNextCommand.NotifyCanExecuteChanged();
        CreateEnhancementAndNextCommand.NotifyCanExecuteChanged();
        CreateRecipeAndNextCommand.NotifyCanExecuteChanged();
    }

    private void RefreshReferenceSearch()
    {
        ReferenceSearchResults.Clear();
        SelectedReference = null;
        if (SelectedObservation is null || string.IsNullOrWhiteSpace(ReferenceSearchText))
        {
            return;
        }

        foreach (var result in _itemReferenceCatalog.Search(
                     ReferenceSearchText,
                     SelectedObservation.FamilyHint,
                     maximumResults: 30))
        {
            ReferenceSearchResults.Add(new ItemReferenceSearchRowViewModel(result));
        }
    }

    private void RefreshProducedEnhancementSearch()
    {
        ProducedEnhancementSearchResults.Clear();
        SelectedProducedEnhancement = null;
        if (SelectedObservation?.FamilyHint is not ReferenceItemFamily.Recipe
            || string.IsNullOrWhiteSpace(ProducedEnhancementSearchText))
        {
            return;
        }

        foreach (var result in _itemReferenceCatalog.Search(
                     ProducedEnhancementSearchText,
                     ReferenceItemFamily.Enhancement,
                     maximumResults: 30))
        {
            ProducedEnhancementSearchResults.Add(new ItemReferenceSearchRowViewModel(result));
        }
    }

    private void ExecuteClassificationAndAdvance(Func<AcquisitionClassificationResult> execute)
    {
        var selectedIndex = Math.Max(0, NeedsClassification.IndexOf(SelectedObservation!));
        _classificationCommandInProgress = true;
        _queueRefreshPending = false;
        AcquisitionClassificationResult result;
        try
        {
            result = execute();
        }
        finally
        {
            _classificationCommandInProgress = false;
        }

        if (result.IsSuccess)
        {
            _queueRefreshPending = false;
            RefreshObservationQueue(selectedIndex);
        }
        else if (_queueRefreshPending)
        {
            _queueRefreshPending = false;
            RefreshObservationQueue();
        }

        ClassificationStatusMessage = result.IsSuccess
            ? $"Saved {result.CatalogItemId}."
            : result.Detail ?? result.Outcome.ToString();
    }

    private static string StripRecipeSuffix(string text)
    {
        const string suffix = "(Recipe)";
        return text.EndsWith(suffix, StringComparison.Ordinal)
            ? text[..^suffix.Length].TrimEnd()
            : text;
    }

    private void RefreshSelectedObservationDetail(AcquisitionObservationRowViewModel? selected)
    {
        SelectedEvidence.Clear();
        if (selected is null)
        {
            SelectedFirstSeenLabel = "—";
            SelectedLastSeenLabel = "—";
            SelectedFailedVersionsLabel = "—";
            SelectedDeveloperNotes = string.Empty;
            return;
        }

        var detail = _observationService.GetObservationDetail(selected.NormalizedKey);
        if (detail is null)
        {
            return;
        }

        SelectedFirstSeenLabel = detail.Summary.FirstSeenUtc.ToLocalTime().ToString("g");
        SelectedLastSeenLabel = detail.Summary.LastSeenUtc.ToLocalTime().ToString("g");
        SelectedFailedVersionsLabel = detail.FailedCatalogVersions.Count == 0
            ? "—"
            : string.Join(", ", detail.FailedCatalogVersions);
        SelectedDeveloperNotes = detail.DeveloperNotes ?? string.Empty;

        foreach (var evidence in detail.Evidence)
        {
            SelectedEvidence.Add(new AcquisitionEvidenceRowViewModel(evidence));
        }
    }
}

public sealed record ObservationFamilyFilterOption(ReferenceItemFamily? Family, string Label);

public sealed class AcquisitionObservationRowViewModel
{
    public AcquisitionObservationRowViewModel(AcquisitionObservationListItem item)
    {
        NormalizedKey = item.NormalizedKey;
        ObservedText = item.ObservedText;
        FamilyHint = item.FamilyHint;
        ResolutionState = item.ResolutionState;
        OccurrenceCount = item.OccurrenceCount;
        FirstSeenUtc = item.FirstSeenUtc;
        LastSeenUtc = item.LastSeenUtc;
    }

    public string NormalizedKey { get; }

    public string ObservedText { get; }

    public ReferenceItemFamily? FamilyHint { get; }

    public AcquisitionIdentityResolutionState ResolutionState { get; }

    public long OccurrenceCount { get; }

    public DateTimeOffset FirstSeenUtc { get; }

    public DateTimeOffset LastSeenUtc { get; }

    public string FamilyLabel => FamilyHint?.ToString() ?? "Presentation Unknown";

    public string PresentationStatusLabel =>
        ResolutionState is AcquisitionIdentityResolutionState.PresentedUnresolved
            ? "Presentation Known"
            : "Presentation Unknown";

    public string CatalogStatusLabel => "Catalog Missing";

    public string LastSeenLabel => LastSeenUtc.ToLocalTime().ToString("g");

    public string CountLabel => $"×{OccurrenceCount:N0}";
}

public sealed class AcquisitionEvidenceRowViewModel
{
    public AcquisitionEvidenceRowViewModel(AcquisitionObservationEvidenceSample sample)
    {
        ObservedText = sample.ObservedText;
        CapturedAtLabel = sample.CapturedAtUtc.ToLocalTime().ToString("g");
        GrammarSource = sample.GrammarSource;
        CatalogVersion = string.IsNullOrWhiteSpace(sample.FailedCatalogVersion)
            ? "—"
            : sample.FailedCatalogVersion;
    }

    public string ObservedText { get; }

    public string CapturedAtLabel { get; }

    public string GrammarSource { get; }

    public string CatalogVersion { get; }
}

public sealed class ItemReferenceSearchRowViewModel
{
    public ItemReferenceSearchRowViewModel(ItemReferenceSearchResult result)
    {
        CatalogItemId = result.CatalogItemId;
        Family = result.Family;
        CurrentDisplayName = result.CurrentDisplayName;
        Subtype = result.Subtype;
        EnhancementSetName = result.EnhancementSetName;
        EnhancementSetId = result.EnhancementSetId;
        MatchedText = result.MatchedText;
    }

    public string CatalogItemId { get; }

    public ReferenceItemFamily Family { get; }

    public string CurrentDisplayName { get; }

    public string Subtype { get; }

    public string? EnhancementSetName { get; }

    public string? EnhancementSetId { get; }

    public string? MatchedText { get; }

    public string SecondaryLabel => string.Join(
        " · ",
        new[] { CatalogItemId, Family.ToString(), Subtype, EnhancementSetName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}
