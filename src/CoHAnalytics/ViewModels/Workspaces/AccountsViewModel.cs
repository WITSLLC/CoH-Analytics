using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Accounts;
using CoHAnalytics.ViewModels.CharacterIcons;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

public partial class AccountsViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private readonly HomecomingAccountDiscoveryService _accountDiscoveryService;
    private readonly ICharacterRepository _characterRepository;
    private readonly IMonitoringSessionManager _monitoringSessionManager;
    private readonly IGameplaySessionManager _gameplaySessionManager;
    private readonly IGameplaySessionIdentityReadService _identityReadService;
    private readonly IViewedContextService _viewedContextService;
    private readonly ILogActivityService _logActivityService;
    private readonly AccountAnonymityService _accountAnonymityService;
    private readonly CharacterBuildImportService _characterBuildImportService;
    private readonly CharacterBadgeBuildSyncService _characterBadgeBuildSyncService;
    private readonly ICharacterBadgeAcquisitionRepository _characterBadgeAcquisitionRepository;
    private readonly IItemReferenceCatalog _itemReferenceCatalog;
    private readonly IInstalledGameAssetProvider? _installedGameAssetProvider;
    private readonly IBuiltInCharacterIconService _builtInCharacterIconService;
    private readonly ICustomCharacterIconService _customCharacterIconService;
    private readonly Dictionary<string, (string ObservedTitle, CharacterBadgeAcquisitionProvenance Provenance)>
        _displayedCharacterBadges = new(StringComparer.Ordinal);
    private CharacterRecordId? _displayedCharacterBadgeRecordId;
    private string? _pendingSelectionStableId;
    private bool _applyingViewedContext;
    private bool _disposed;

    public AccountsViewModel(
        HomecomingAccountDiscoveryService accountDiscoveryService,
        ICharacterRepository characterRepository,
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        IMonitoringSessionManager monitoringSessionManager,
        IGameplaySessionManager gameplaySessionManager,
        IGameplaySessionIdentityReadService identityReadService,
        IViewedContextService viewedContextService,
        ILogActivityService logActivityService,
        CharacterBuildImportService characterBuildImportService,
        AccountAnonymityService? accountAnonymityService = null,
        ICharacterBadgeAcquisitionRepository? characterBadgeAcquisitionRepository = null,
        IItemReferenceCatalog? itemReferenceCatalog = null,
        IInstalledGameAssetProvider? installedGameAssetProvider = null,
        IBuiltInCharacterIconService? builtInCharacterIconService = null,
        ICustomCharacterIconService? customCharacterIconService = null)
        : base(orchestrator, gameRuntimeService)
    {
        _accountDiscoveryService = accountDiscoveryService;
        _characterRepository = characterRepository;
        _monitoringSessionManager = monitoringSessionManager;
        _gameplaySessionManager = gameplaySessionManager;
        _identityReadService = identityReadService;
        _viewedContextService = viewedContextService;
        _logActivityService = logActivityService;
        _characterBuildImportService = characterBuildImportService;
        _accountAnonymityService = accountAnonymityService ?? new AccountAnonymityService();
        _characterBadgeAcquisitionRepository =
            characterBadgeAcquisitionRepository ?? NullCharacterBadgeAcquisitionRepository.Instance;
        _itemReferenceCatalog = itemReferenceCatalog
            ?? new FailedItemReferenceCatalog("Catalog not configured.");
        _characterBadgeBuildSyncService = new CharacterBadgeBuildSyncService(
            characterRepository,
            _characterBadgeAcquisitionRepository,
            _itemReferenceCatalog);
        _installedGameAssetProvider = installedGameAssetProvider;
        _builtInCharacterIconService = builtInCharacterIconService
            ?? NullBuiltInCharacterIconService.Instance;
        _customCharacterIconService = customCharacterIconService
            ?? NullCustomCharacterIconService.Instance;

        Accounts = new ObservableCollection<AccountListItemViewModel>();
        AccountCharacters = new ObservableCollection<AccountCharacterCardViewModel>();
        CharacterBadgeCategories = new ObservableCollection<CharacterBadgeCategoryGroupViewModel>();
        WorkspaceChips = CreateWorkspaceChips();

        _characterRepository.StateChanged += OnCharacterRepositoryChanged;
        Orchestrator.SnapshotChanged += OnAccountsOrchestratorChanged;
        WireEnvironmentStatus();
        _monitoringSessionManager.StateChanged += OnMonitoringChanged;
        _gameplaySessionManager.StateChanged += OnGameplaySessionChanged;
        _identityReadService.Changed += OnIdentityReadChanged;
        _viewedContextService.Changed += OnViewedContextChanged;
        _logActivityService.ActivityChanged += OnLogActivityChanged;
        _accountAnonymityService.Changed += OnAccountAnonymityChanged;

        RefreshAccounts();
        ApplyViewedContext();
        ApplyPresentationState();
    }

    public override string Title => "Accounts";

    public string Subtitle => "All Homecoming accounts discovered on this machine";

    public ObservableCollection<AccountListItemViewModel> Accounts { get; }

    public ObservableCollection<AccountCharacterCardViewModel> AccountCharacters { get; }

    public ObservableCollection<AccountWorkspaceChipViewModel> WorkspaceChips { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAccount))]
    [NotifyPropertyChangedFor(nameof(ShowDetailPanel))]
    [NotifyPropertyChangedFor(nameof(ShowCharacterArea))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyCharacterState))]
    private AccountListItemViewModel? _selectedAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBannerMessage))]
    private string? _bannerMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowAccountList))]
    private bool _hasAccounts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDiscoveryError))]
    private bool _discoveryFailed;

    [ObservableProperty]
    private AccountDetailsViewModel? _selectedAccountDetails;

    [ObservableProperty]
    private string _heroAccountName = string.Empty;

    [ObservableProperty]
    private string _heroCharacterCountLabel = string.Empty;

    [ObservableProperty]
    private string _heroLastPlayedValue = "Not yet observed";

    [ObservableProperty]
    private string _heroActiveCharacterValue = "Unknown";

    [ObservableProperty]
    private bool _heroActiveCharacterIsLive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContextDivergence))]
    private string _heroViewingCharacterLabel = "Viewing: Unknown";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContextDivergence))]
    private string _heroLiveCharacterLabel = "Live: Unknown";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContextDivergence))]
    private bool _showReturnToLive;

    public bool ShowContextDivergence => ShowReturnToLive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCharacterBuildSaveCommand))]
    [NotifyPropertyChangedFor(nameof(ShowCharacterDetail))]
  [NotifyPropertyChangedFor(nameof(ShowCharacterSelector))]
    private AccountCharacterCardViewModel? _selectedCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverviewTabContent))]
    [NotifyPropertyChangedFor(nameof(ShowBadgesTabContent))]
    private AccountCharacterDetailTab _selectedCharacterTab = AccountCharacterDetailTab.Overview;

    [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(ShowCharacterHeaderLevel))]
    private string? _characterHeaderLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCharacterHeaderPowersetLine))]
    private string? _characterHeaderArchetypeLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCharacterHeaderPowersetLine))]
    private string? _characterHeaderPowersetPair;

    [ObservableProperty]
    private string _characterHeaderName = string.Empty;

    [ObservableProperty]
    private string _characterHeaderLastPlayedValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCharacterHeaderIcon))]
    [NotifyPropertyChangedFor(nameof(ShowCharacterHeaderIconPlaceholder))]
    private ImageSource? _characterHeaderIconSource;

    [ObservableProperty]
    private string? _characterHeaderIconId;

    [ObservableProperty]
    private string _overviewAccountName = string.Empty;

    [ObservableProperty]
    private string? _overviewCharacterShortId;

    [ObservableProperty]
    private string _overviewLastPlayed = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBuildMetadataUpdatedLabel))]
    private string? _buildMetadataUpdatedLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBuildMetadataNotImported))]
    [NotifyPropertyChangedFor(nameof(ShowBuildMetadataImported))]
    [NotifyPropertyChangedFor(nameof(RefreshBuildMetadataButtonLabel))]
    [NotifyPropertyChangedFor(nameof(CharacterHeaderBuildValue))]
    private bool _hasImportedBuildMetadata;

    [ObservableProperty]
    private string? _buildImportStatusMessage;

    [ObservableProperty]
    private string? _badgeSyncStatusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCharacterBadgeEmptyState))]
    private string _characterBadgeCountLabel = string.Empty;

    [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(ShowCharacterBadgeGallery))]
    private bool _showCharacterBadgeEmptyState;

    [ObservableProperty]
    private int _characterBadgeOtherCategoryCount;

    public ObservableCollection<CharacterBadgeCategoryGroupViewModel> CharacterBadgeCategories { get; }

    public bool ShowCharacterBadgeGallery => !ShowCharacterBadgeEmptyState;

    public string? SelectedCharacterBuildSaveCommand => SelectedCharacter?.BuildSaveCommand;

    public bool HasSelectedAccount => SelectedAccount is not null;

    public bool ShowDetailPanel => HasSelectedAccount && !ShowDiscoveryError;

    public bool ShowCharacterArea => HasSelectedAccount && !ShowDiscoveryError;

    public bool ShowEmptyState => !DiscoveryFailed && !HasAccounts;

    public bool ShowAccountList => !DiscoveryFailed && HasAccounts;

    public bool ShowDiscoveryError => DiscoveryFailed;

    public bool ShowBannerMessage => !string.IsNullOrWhiteSpace(BannerMessage);

    public bool ShowEmptyCharacterState => ShowCharacterArea && AccountCharacters.Count == 0;

    public bool ShowCharacterDetail => ShowCharacterArea && SelectedCharacter is not null;

    public bool ShowCharacterSelector => ShowCharacterArea && AccountCharacters.Count > 0;

    public bool ShowCharacterHeaderLevel => !string.IsNullOrWhiteSpace(CharacterHeaderLevel);

    public bool ShowCharacterHeaderPowersetLine =>
        !string.IsNullOrWhiteSpace(CharacterHeaderArchetypeLabel)
        && !string.IsNullOrWhiteSpace(CharacterHeaderPowersetPair);

    public bool ShowCharacterHeaderIcon => CharacterHeaderIconSource is not null;

    public bool ShowCharacterHeaderIconPlaceholder => !ShowCharacterHeaderIcon;

    public bool ShowOverviewTabContent => SelectedCharacterTab == AccountCharacterDetailTab.Overview;

    public bool ShowBadgesTabContent => SelectedCharacterTab == AccountCharacterDetailTab.Badges;

    public bool ShowOverviewPowersetLine => ShowCharacterHeaderPowersetLine;

    public bool ShowBuildMetadataUpdatedLabel => !string.IsNullOrWhiteSpace(BuildMetadataUpdatedLabel);

    public bool ShowBuildMetadataNotImported => !HasImportedBuildMetadata;

    public bool ShowBuildMetadataImported => HasImportedBuildMetadata;

    public string RefreshBuildMetadataButtonLabel =>
        HasImportedBuildMetadata ? "Refresh From Game" : "Import / Refresh";

    public string CharacterHeaderBuildValue =>
        HasImportedBuildMetadata ? "Imported" : "Not imported";

    public CharacterIconPickerViewModel? CreateCharacterIconPicker()
    {
        if (!ShowCharacterDetail || _builtInCharacterIconService.Icons.Count == 0)
        {
            return null;
        }

        var currentReference = SelectedCharacter is not null
            && Guid.TryParse(SelectedCharacter.RecordId, out var recordGuid)
            ? _characterRepository.TryGetRecord(CharacterRecordId.FromGuid(recordGuid))?.IconReference
            : null;
        return new CharacterIconPickerViewModel(
            _builtInCharacterIconService.Icons,
            _customCharacterIconService.GetIcons(),
            currentReference,
            _customCharacterIconService,
            GetCharactersUsingCustomIcon);
    }

    public bool ApplyCharacterIconSelection(string? iconId)
    {
        if (!_builtInCharacterIconService.TryGetIcon(iconId ?? string.Empty, out _))
        {
            return false;
        }

        return ApplyCharacterIconSelection(CharacterIconReference.BuiltIn(iconId!));
    }

    private IReadOnlyList<string> GetCharactersUsingCustomIcon(string iconId) =>
        _characterRepository.Current.Records
            .Where(record => record.IconReference is
            {
                Kind: CharacterIconKind.Custom,
                IconId: var assignedIconId
            } && string.Equals(assignedIconId, iconId, StringComparison.Ordinal))
            .Select(record => record.CurrentDisplayName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool ApplyCharacterIconSelection(CharacterIconReference? iconReference)
    {
        if (SelectedCharacter is null
            || !Guid.TryParse(SelectedCharacter.RecordId, out var recordGuid)
            || iconReference is null
            || !CanResolveIcon(iconReference, out _))
        {
            return false;
        }

        var result = _characterRepository.SetIconReference(
            CharacterRecordId.FromGuid(recordGuid),
            iconReference);
        if (!result.IsSuccess)
        {
            return false;
        }

        UpdateCharacterDetailPresentation();
        return true;
    }

    public string EmptyStateMessage =>
        "No Homecoming accounts were found.\nLaunch Homecoming and sign into an account to create its local account folder.";

    public string DiscoveryErrorMessage =>
        "Homecoming accounts could not be discovered. Verify the Homecoming installation and try again later.";

    public string EmptyCharacterStateMessage =>
        "No characters have been discovered for this account yet.\nCharacters will appear after trusted identity evidence is observed.";

    public void RequestAccountSelection(string? accountStableId)
    {
        _pendingSelectionStableId = accountStableId;
        ApplyPendingSelection();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnwireEnvironmentStatus();
        _characterRepository.StateChanged -= OnCharacterRepositoryChanged;
        Orchestrator.SnapshotChanged -= OnAccountsOrchestratorChanged;
        _monitoringSessionManager.StateChanged -= OnMonitoringChanged;
        _gameplaySessionManager.StateChanged -= OnGameplaySessionChanged;
        _identityReadService.Changed -= OnIdentityReadChanged;
        _viewedContextService.Changed -= OnViewedContextChanged;
        _logActivityService.ActivityChanged -= OnLogActivityChanged;
        _accountAnonymityService.Changed -= OnAccountAnonymityChanged;
    }

    partial void OnSelectedAccountChanged(AccountListItemViewModel? value)
    {
        SelectedAccountDetails = value is null
            ? null
            : new AccountDetailsViewModel(value.Account, _accountAnonymityService);
        BannerMessage = null;
        SelectedCharacterTab = AccountCharacterDetailTab.Overview;
        RefreshAccountCharacters();
        UpdateHeroBanner();
        UpdateAccountLiveStates();
    }

    partial void OnSelectedCharacterChanged(AccountCharacterCardViewModel? value)
    {
        foreach (var character in AccountCharacters)
        {
            character.IsViewing = ReferenceEquals(character, value);
        }

        if (!_applyingViewedContext && value is not null && SelectedAccount is not null)
        {
            SelectCharacterCommand.Execute(value);
        }

        BadgeSyncStatusMessage = null;
        UpdateCharacterDetailPresentation();
    }

    [RelayCommand]
    private void ReturnToLive()
    {
        _viewedContextService.ReturnToLive();
    }

    [RelayCommand]
    private void SelectAccount(AccountListItemViewModel? account)
    {
        if (account is null)
        {
            return;
        }

        _viewedContextService.SelectViewedAccount(account.StableId);
    }

    [RelayCommand]
    private void SelectCharacter(AccountCharacterCardViewModel? character)
    {
        if (character is null || SelectedAccount is null)
        {
            return;
        }

        if (!Guid.TryParse(character.RecordId, out var recordGuid))
        {
            return;
        }

        _viewedContextService.SelectViewedCharacter(
            SelectedAccount.StableId,
            CharacterRecordId.FromGuid(recordGuid));
    }

    [RelayCommand]
    private void SelectCharacterTab(AccountCharacterDetailTab tab)
    {
        SelectedCharacterTab = tab;
    }

    [RelayCommand]
    private void CopyBuildSaveCommand()
    {
        if (string.IsNullOrWhiteSpace(SelectedCharacterBuildSaveCommand))
        {
            return;
        }

        Clipboard.SetText(SelectedCharacterBuildSaveCommand);
        BuildImportStatusMessage = null;
    }

    [RelayCommand]
    private void RefreshBuildMetadata()
    {
        if (SelectedAccount is null || SelectedCharacter is null)
        {
            return;
        }

        if (!Guid.TryParse(SelectedCharacter.RecordId, out var recordGuid))
        {
            return;
        }

        var recordId = CharacterRecordId.FromGuid(recordGuid);
        var result = _characterBuildImportService.TryImportIfPresent(
            SelectedAccount.StableId,
            SelectedAccount.Account.FolderPath,
            recordId);

        switch (result.Status)
        {
            case CharacterBuildImportStatus.Imported:
                BuildImportStatusMessage = null;
                RefreshAccountCharacters();
                break;
            case CharacterBuildImportStatus.MissingFile:
                BuildImportStatusMessage = "No buildsave file found yet.";
                break;
            default:
                BuildImportStatusMessage = "Build metadata could not be refreshed.";
                break;
        }

        UpdateCharacterDetailPresentation();
    }

    [RelayCommand]
    private void SyncBadgesFromBuild()
    {
        if (SelectedAccount is null || SelectedCharacter is null)
        {
            return;
        }

        if (!Guid.TryParse(SelectedCharacter.RecordId, out var recordGuid))
        {
            return;
        }

        var result = _characterBadgeBuildSyncService.SyncFromBuild(
            SelectedAccount.StableId,
            SelectedAccount.Account.FolderPath,
            CharacterRecordId.FromGuid(recordGuid));

        BadgeSyncStatusMessage = result.FormatUserMessage();
        UpdateCharacterDetailPresentation();
    }

    private void OnCharacterRepositoryChanged(object? sender, CharacterRepositoryChangedEventArgs e) =>
        DispatchRefresh(RefreshAccountCharacters);

    private void OnAccountsOrchestratorChanged(object? sender, ApplicationStateChangedEventArgs e) =>
        DispatchRefresh(RefreshAccountsPresentation);

    private void OnMonitoringChanged(object? sender, MonitoringSessionManagerChangedEventArgs e) =>
        DispatchRefresh(ApplyPresentationState);

    private void OnGameplaySessionChanged(object? sender, GameplaySessionManagerChangedEventArgs e) =>
        DispatchRefresh(ApplyPresentationState);

    private void OnIdentityReadChanged(object? sender, GameplaySessionIdentityReadModelChangedEventArgs e) =>
        DispatchRefresh(ApplyPresentationState);

    private void OnViewedContextChanged(object? sender, ViewedContextChangedEventArgs e) =>
        DispatchRefresh(ApplyViewedContext);

    private void OnLogActivityChanged(object? sender, LogActivityChangedEventArgs e) =>
        DispatchRefresh(ApplyPresentationState);

    private void OnAccountAnonymityChanged(object? sender, EventArgs e) =>
        DispatchRefresh(RefreshAccountIdentityPresentation);

    private void RefreshAccountIdentityPresentation()
    {
        foreach (var account in Accounts)
        {
            account.RefreshDisplayName(_accountAnonymityService);
        }

        SelectedAccountDetails = SelectedAccount is null
            ? null
            : new AccountDetailsViewModel(SelectedAccount.Account, _accountAnonymityService);
        UpdateHeroBanner();
        UpdateCharacterDetailPresentation();
    }

    private void DispatchRefresh(Action refreshAction)
    {
        if (_disposed)
        {
            return;
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(refreshAction);
            return;
        }

        refreshAction();
    }

    private void ApplyViewedContext()
    {
        var state = _viewedContextService.Current;

        if (state.HasAccount && HasAccounts && !DiscoveryFailed)
        {
            var account = Accounts.FirstOrDefault(item =>
                string.Equals(item.StableId, state.AccountStableId, StringComparison.OrdinalIgnoreCase));
            if (account is not null && !ReferenceEquals(account, SelectedAccount))
            {
                SelectedAccount = account;
            }
        }

        RefreshAccountCharacters();

        _applyingViewedContext = true;
        try
        {
            if (state.CharacterRecordId is not null)
            {
                var recordId = state.CharacterRecordId.ToString();
                SelectedCharacter = AccountCharacters.FirstOrDefault(card =>
                    string.Equals(card.RecordId, recordId, StringComparison.Ordinal));
            }
            else
            {
                SelectedCharacter = ResolveDefaultCharacterSelection();
            }
        }
        finally
        {
            _applyingViewedContext = false;
        }

        UpdateHeroBanner();
        UpdateAccountLiveStates();
        UpdateCharacterDetailPresentation();
    }

    private void ApplyPresentationState()
    {
        RefreshAccountsPresentation();
        UpdateCharacterDetailPresentation();
    }

    private void RefreshAccountsPresentation()
    {
        UpdateAccountLiveStates();
        UpdateHeroBanner();
        RefreshAccountCharacters();
    }

    private void RefreshAccounts()
    {
        var discoveredAccounts = HomecomingAccountPresentation.OrderForAccountsWorkspace(
            _accountDiscoveryService.Discover());

        DiscoveryFailed = _accountDiscoveryService.LastDiscovery.Attempts.Any(attempt =>
            !attempt.IsValid
            && attempt.FailureReason?.Contains("Unable to enumerate", StringComparison.OrdinalIgnoreCase) == true);

        Accounts.Clear();
        foreach (var account in discoveredAccounts)
        {
            Accounts.Add(new AccountListItemViewModel(account, _accountAnonymityService));
        }

        HasAccounts = Accounts.Count > 0;

        if (DiscoveryFailed)
        {
            SelectedAccount = null;
            BannerMessage = null;
            return;
        }

        ApplyPendingSelection();

        if (SelectedAccount is null && HasAccounts)
        {
            SelectedAccount = Accounts[0];
        }

        UpdateAccountLiveStates();
        UpdateHeroBanner();
        RefreshAccountCharacters();
    }

    private void ApplyPendingSelection()
    {
        if (string.IsNullOrWhiteSpace(_pendingSelectionStableId))
        {
            return;
        }

        var requestedStableId = _pendingSelectionStableId;
        _pendingSelectionStableId = null;

        var match = Accounts.FirstOrDefault(account =>
            string.Equals(account.StableId, requestedStableId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            BannerMessage = "The selected account is no longer available.";
            return;
        }

        SelectedAccount = match;
        BannerMessage = null;
    }

    private void UpdateAccountLiveStates()
    {
        foreach (var account in Accounts)
        {
            account.IsSelected = ReferenceEquals(account, SelectedAccount);
            account.IsLive = IsAccountLive(account.StableId);
            account.CharacterCount = CountCharactersForAccount(account.StableId);
            account.ActivitySummary = BuildAccountActivitySummary(account.Account);
            account.StatusLabel = account.IsLive ? "LIVE" : "OFFLINE";
            account.LogAvailabilityLabel = account.Account.HasLogsFolder
                ? account.Account.HasHistoricalLogs ? "Logs available" : "Logs folder present"
                : "No logs folder";
        }
    }

    private void UpdateHeroBanner()
    {
        if (SelectedAccount is null)
        {
            HeroAccountName = string.Empty;
            HeroCharacterCountLabel = string.Empty;
            HeroLastPlayedValue = "Not yet observed";
            HeroActiveCharacterValue = "Unknown";
            HeroActiveCharacterIsLive = false;
            ShowReturnToLive = false;
            HeroViewingCharacterLabel = "Viewing: Unknown";
            HeroLiveCharacterLabel = "Live: Unknown";
            return;
        }

        var account = SelectedAccount.Account;
        var characterCount = CountCharactersForAccount(account.StableId);
        HeroAccountName = _accountAnonymityService.MaskForPresentation(account.DisplayName);
        HeroCharacterCountLabel = $"{characterCount} Character{(characterCount == 1 ? string.Empty : "s")}";
        HeroLastPlayedValue = FormatActivityTimestamp(GetAccountLastActivity(account));

        var (activeRecordId, activeName, isLive) = GetConfirmedActiveCharacterForAccount(account.StableId);
        var viewed = _viewedContextService.Current;
        var viewedName = viewed.CharacterRecordId is not null
            && string.Equals(viewed.AccountStableId, account.StableId, StringComparison.OrdinalIgnoreCase)
            ? _characterRepository.TryGetRecord(viewed.CharacterRecordId)?.CurrentDisplayName
            : null;

        HeroViewingCharacterLabel = string.IsNullOrWhiteSpace(viewedName)
            ? "Viewing: Unknown"
            : $"Viewing: {viewedName}";

        HeroLiveCharacterLabel = activeRecordId is null || string.IsNullOrWhiteSpace(activeName)
            ? "Live: Unknown"
            : $"Live: {activeName}";

        ShowReturnToLive = ComputeShowReturnToLive(viewed, activeRecordId);

        HeroActiveCharacterValue = string.IsNullOrWhiteSpace(viewedName)
            ? "Unknown"
            : viewedName;

        HeroActiveCharacterIsLive = isLive && activeRecordId is not null;
    }

    private bool ComputeShowReturnToLive(ViewedContextState viewed, CharacterRecordId? liveRecordId)
    {
        if (viewed.IsFollowingLive || SelectedAccount is null)
        {
            return false;
        }

        var hasLiveSession = _identityReadService.Current.Contexts.Any(context =>
            context.IsConfirmed && context.HasActiveSession);
        if (!hasLiveSession || liveRecordId is null)
        {
            return false;
        }

        if (!string.Equals(viewed.AccountStableId, SelectedAccount.StableId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return viewed.CharacterRecordId is null || viewed.CharacterRecordId != liveRecordId;
    }

    private void RefreshAccountCharacters()
    {
        if (SelectedAccount is null)
        {
            AccountCharacters.Clear();
            SelectedCharacter = null;
            OnPropertyChanged(nameof(ShowEmptyCharacterState));
            OnPropertyChanged(nameof(ShowCharacterDetail));
            OnPropertyChanged(nameof(ShowCharacterSelector));
            UpdateCharacterDetailPresentation();
            return;
        }

        var accountStableId = SelectedAccount.StableId;
        var activeCharacter = GetConfirmedActiveCharacterForAccount(accountStableId);
        var viewed = _viewedContextService.Current;

        var characters = _characterRepository.Current.Records
            .Where(record => string.Equals(record.AccountStableId, accountStableId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.LastObservedAt != default)
            .ThenByDescending(record => record.LastObservedAt)
            .ThenBy(record => record.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(record => CreateCharacterCard(record, activeCharacter, viewed, accountStableId))
            .ToList();

        if (CanRefreshCharacterCardsInPlace(characters))
        {
            for (var index = 0; index < characters.Count; index++)
            {
                AccountCharacters[index].IsLive = characters[index].IsLive;
                AccountCharacters[index].IsViewing = characters[index].IsViewing;
            }
        }
        else
        {
            AccountCharacters.Clear();
            foreach (var character in characters)
            {
                AccountCharacters.Add(character);
            }
        }

        if (SelectedCharacter is not null)
        {
            var match = AccountCharacters.FirstOrDefault(card =>
                string.Equals(card.RecordId, SelectedCharacter.RecordId, StringComparison.Ordinal));
            if (match is not null)
            {
                _applyingViewedContext = true;
                try
                {
                    SelectedCharacter = match;
                }
                finally
                {
                    _applyingViewedContext = false;
                }
            }
            else
            {
                SelectedCharacter = ResolveDefaultCharacterSelection();
            }
        }
        else
        {
            SelectedCharacter = ResolveDefaultCharacterSelection();
        }

        OnPropertyChanged(nameof(ShowEmptyCharacterState));
        OnPropertyChanged(nameof(ShowCharacterDetail));
        OnPropertyChanged(nameof(ShowCharacterSelector));
        UpdateCharacterDetailPresentation();
    }

    private bool CanRefreshCharacterCardsInPlace(IReadOnlyList<AccountCharacterCardViewModel> characters)
    {
        if (AccountCharacters.Count != characters.Count)
        {
            return false;
        }

        for (var index = 0; index < characters.Count; index++)
        {
            var current = AccountCharacters[index];
            var updated = characters[index];
            if (!string.Equals(current.RecordId, updated.RecordId, StringComparison.Ordinal)
                || !string.Equals(current.DisplayName, updated.DisplayName, StringComparison.Ordinal)
                || !string.Equals(current.CharacterShortId, updated.CharacterShortId, StringComparison.Ordinal)
                || !string.Equals(current.BuildSaveCommand, updated.BuildSaveCommand, StringComparison.Ordinal)
                || !string.Equals(current.PortraitPath, updated.PortraitPath, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private AccountCharacterCardViewModel? ResolveDefaultCharacterSelection()
    {
        if (SelectedAccount is null || AccountCharacters.Count == 0)
        {
            return null;
        }

        var viewed = _viewedContextService.Current;
        if (viewed.CharacterRecordId is not null
            && string.Equals(viewed.AccountStableId, SelectedAccount.StableId, StringComparison.OrdinalIgnoreCase))
        {
            var viewedCard = AccountCharacters.FirstOrDefault(card =>
                string.Equals(card.RecordId, viewed.CharacterRecordId.ToString(), StringComparison.Ordinal));
            if (viewedCard is not null)
            {
                return viewedCard;
            }
        }

        var (activeRecordId, _, _) = GetConfirmedActiveCharacterForAccount(SelectedAccount.StableId);
        if (activeRecordId is not null)
        {
            var liveCard = AccountCharacters.FirstOrDefault(card =>
                string.Equals(card.RecordId, activeRecordId.ToString(), StringComparison.Ordinal));
            if (liveCard is not null)
            {
                return liveCard;
            }
        }

        return AccountCharacters[0];
    }

    private void UpdateCharacterDetailPresentation()
    {
        if (SelectedAccount is null || SelectedCharacter is null)
        {
            CharacterHeaderName = string.Empty;
            CharacterHeaderLevel = null;
            CharacterHeaderArchetypeLabel = null;
            CharacterHeaderPowersetPair = null;
            CharacterHeaderLastPlayedValue = string.Empty;
            CharacterHeaderIconId = null;
            CharacterHeaderIconSource = null;
            OverviewAccountName = string.Empty;
            OverviewCharacterShortId = null;
            OverviewLastPlayed = string.Empty;
            BuildMetadataUpdatedLabel = null;
            HasImportedBuildMetadata = false;
            BadgeSyncStatusMessage = null;
            ClearCharacterBadgeGallery();
            return;
        }

        if (!Guid.TryParse(SelectedCharacter.RecordId, out var recordGuid))
        {
            ClearCharacterBadgeGallery();
            return;
        }

        var record = _characterRepository.TryGetRecord(CharacterRecordId.FromGuid(recordGuid));
        if (record is null)
        {
            return;
        }

        CharacterHeaderName = record.CurrentDisplayName;
        CharacterHeaderLevel = record.ObservedLevel is int level ? $"Level {level}" : null;
        if (TryFormatPowersetPresentation(record, out var archetypeLabel, out var powersetPair))
        {
            CharacterHeaderArchetypeLabel = archetypeLabel;
            CharacterHeaderPowersetPair = powersetPair;
        }
        else
        {
            CharacterHeaderArchetypeLabel = null;
            CharacterHeaderPowersetPair = null;
        }

        CharacterHeaderLastPlayedValue = FormatActivityTimestamp(record.LastObservedAt);
        CharacterHeaderIconId = record.IconReference?.IconId;
        CharacterHeaderIconSource = record.IconReference is not null
            && CanResolveIcon(record.IconReference, out var iconSource)
                ? iconSource
                : null;

        OverviewAccountName = _accountAnonymityService.MaskForPresentation(SelectedAccount.Account.DisplayName);
        OverviewCharacterShortId = record.CharacterShortId;
        OverviewLastPlayed = FormatActivityTimestamp(record.LastObservedAt);
        HasImportedBuildMetadata = RecordHasImportedBuildMetadata(record);
        BuildMetadataUpdatedLabel = record.BuildMetadataObservedAt is DateTimeOffset observedAt
            ? $"Updated: {FormatActivityTimestamp(observedAt)}"
            : null;

        UpdateCharacterBadgeGallery(record.RecordId);
    }

    private bool CanResolveIcon(CharacterIconReference iconReference, out ImageSource imageSource)
    {
        if (iconReference.Kind == CharacterIconKind.BuiltIn
            && _builtInCharacterIconService.TryGetIcon(iconReference.IconId, out var builtIn))
        {
            imageSource = builtIn.ImageSource;
            return true;
        }

        if (iconReference.Kind == CharacterIconKind.Custom
            && _customCharacterIconService.TryGetIcon(iconReference.IconId, out var custom))
        {
            imageSource = custom.ImageSource;
            return true;
        }

        imageSource = null!;
        return false;
    }

    private void ClearCharacterBadgeGallery()
    {
        _displayedCharacterBadgeRecordId = null;
        _displayedCharacterBadges.Clear();
        CharacterBadgeCountLabel = string.Empty;
        ShowCharacterBadgeEmptyState = false;
        CharacterBadgeOtherCategoryCount = 0;
        CharacterBadgeCategories.Clear();
    }

    private void UpdateCharacterBadgeGallery(CharacterRecordId characterRecordId)
    {
        var acquisitions = _characterBadgeAcquisitionRepository.GetSnapshot(characterRecordId).Acquisitions;
        if (_displayedCharacterBadgeRecordId == characterRecordId
            && _displayedCharacterBadges.Count == acquisitions.Count
            && acquisitions.All(acquisition =>
                _displayedCharacterBadges.TryGetValue(acquisition.CatalogItemId, out var displayed)
                && string.Equals(displayed.ObservedTitle, acquisition.ObservedTitle, StringComparison.Ordinal)
                && displayed.Provenance == acquisition.Provenance))
        {
            return;
        }

        var gallery = CharacterBadgeGallerySupport.Build(
            acquisitions,
            _itemReferenceCatalog,
            _installedGameAssetProvider);

        _displayedCharacterBadgeRecordId = characterRecordId;
        _displayedCharacterBadges.Clear();
        foreach (var acquisition in acquisitions)
        {
            _displayedCharacterBadges[acquisition.CatalogItemId] =
                (acquisition.ObservedTitle, acquisition.Provenance);
        }
        CharacterBadgeCountLabel = gallery.CountLabel;
        ShowCharacterBadgeEmptyState = gallery.TotalCount == 0;
        CharacterBadgeOtherCategoryCount = gallery.OtherCategoryCount;
        CharacterBadgeCategories.Clear();

        foreach (var category in gallery.Categories)
        {
            var badges = category.Badges
                .Select(badge => new CharacterBadgeIconViewModel(badge))
                .ToArray();
            CharacterBadgeCategories.Add(new CharacterBadgeCategoryGroupViewModel(
                category.CategoryName,
                badges));
        }
    }

    private AccountCharacterCardViewModel CreateCharacterCard(
        CharacterRecord record,
        (CharacterRecordId? RecordId, string? Name, bool IsLive) activeCharacter,
        ViewedContextState viewed,
        string accountStableId)
    {
        var isLive = activeCharacter.IsLive
            && activeCharacter.RecordId is not null
            && record.RecordId == activeCharacter.RecordId;

        var isViewing = viewed.CharacterRecordId is not null
            && record.RecordId == viewed.CharacterRecordId
            && string.Equals(viewed.AccountStableId, accountStableId, StringComparison.OrdinalIgnoreCase);

        return new AccountCharacterCardViewModel
        {
            RecordId = record.RecordId.ToString(),
            DisplayName = record.CurrentDisplayName,
            CharacterShortId = record.CharacterShortId,
            BuildSaveCommand = CharacterShortId.TryParse(record.CharacterShortId, out var shortId)
                ? CharacterShortId.FormatBuildSaveCommand(shortId)
                : null,
            PortraitPath = "Assets/Icons/monitoring/character.svg",
            IsLive = isLive,
            IsViewing = isViewing
        };
    }

    private int CountCharactersForAccount(string accountStableId) =>
        _characterRepository.Current.Records.Count(record =>
            string.Equals(record.AccountStableId, accountStableId, StringComparison.OrdinalIgnoreCase));

    private bool IsAccountLive(string accountStableId)
    {
        if (GameRuntimeService.CurrentStatus != GameRuntimeStatus.Running)
        {
            return false;
        }

        if (_logActivityService.Current.Candidates.Any(candidate =>
                string.Equals(candidate.AccountStableId, accountStableId, StringComparison.OrdinalIgnoreCase)
                && candidate.ActivityState == LogSourceActivityState.Growing))
        {
            return true;
        }

        return _monitoringSessionManager.Current.Contexts.Any(context =>
            string.Equals(context.AccountStableId, accountStableId, StringComparison.OrdinalIgnoreCase)
            && context.State == MonitoringContextState.Ready);
    }

    private (CharacterRecordId? RecordId, string? Name, bool IsLive) GetConfirmedActiveCharacterForAccount(
        string accountStableId)
    {
        var confirmedContext = _identityReadService.Current.Contexts
            .Where(context =>
                string.Equals(context.AccountStableId, accountStableId, StringComparison.OrdinalIgnoreCase)
                && context.IsConfirmed
                && context.HasActiveSession)
            .OrderByDescending(context => context.CharacterRecordId?.Value ?? Guid.Empty)
            .FirstOrDefault();

        if (confirmedContext?.CharacterRecordId is null)
        {
            return (null, null, false);
        }

        var isLive = GameRuntimeService.CurrentStatus == GameRuntimeStatus.Running
            && _monitoringSessionManager.IsRuntimeAvailable;

        return (
            confirmedContext.CharacterRecordId,
            confirmedContext.CharacterDisplayName,
            isLive);
    }

    private DateTimeOffset? GetAccountLastActivity(HomecomingAccount account)
    {
        var characterTimestamps = _characterRepository.Current.Records
            .Where(record => string.Equals(record.AccountStableId, account.StableId, StringComparison.OrdinalIgnoreCase))
            .Select(record => record.LastObservedAt)
            .ToList();

        if (characterTimestamps.Count > 0)
        {
            return characterTimestamps.Max();
        }

        return account.NewestLogTimestamp;
    }

    private static string BuildAccountActivitySummary(HomecomingAccount account)
    {
        if (account.NewestLogTimestamp is { } newestLog)
        {
            return $"Latest log: {FormatActivityTimestamp(newestLog)}";
        }

        return account.HasLogsFolder ? "No historical logs" : "No logs folder";
    }

    private static string FormatActivityTimestamp(DateTimeOffset? timestamp)
    {
        if (timestamp is null || timestamp == default)
        {
            return "Not yet observed";
        }

        var local = timestamp.Value.ToLocalTime();
        var days = (DateTime.Now - local.DateTime).TotalDays;
        if (days < 1)
        {
            return "Today";
        }

        if (days < 2)
        {
            return "Yesterday";
        }

        return local.ToString("yyyy-MM-dd");
    }

    private static bool TryFormatPowersetPresentation(
        CharacterRecord record,
        out string archetypeLabel,
        out string powersetPair)
    {
        archetypeLabel = string.Empty;
        powersetPair = string.Empty;
        if (string.IsNullOrWhiteSpace(record.PrimaryPowerSet)
            || string.IsNullOrWhiteSpace(record.SecondaryPowerSet)
            || string.IsNullOrWhiteSpace(record.Archetype))
        {
            return false;
        }

        archetypeLabel = $"{record.Archetype.Trim()}:";
        powersetPair = $"{record.PrimaryPowerSet} / {record.SecondaryPowerSet}";
        return true;
    }

    private static bool RecordHasImportedBuildMetadata(CharacterRecord record) =>
        !string.IsNullOrWhiteSpace(record.Archetype)
        && !string.IsNullOrWhiteSpace(record.PrimaryPowerSet)
        && !string.IsNullOrWhiteSpace(record.SecondaryPowerSet);

    private static ObservableCollection<AccountWorkspaceChipViewModel> CreateWorkspaceChips() =>
        new([
            new AccountWorkspaceChipViewModel("Characters", "Assets/Icons/monitoring/character.svg", true, false)
        ]);
}

public enum AccountCharacterDetailTab
{
    Overview,
    Badges
}

public sealed partial class AccountListItemViewModel : ObservableObject
{
    public AccountListItemViewModel(
        HomecomingAccount account,
        AccountAnonymityService? accountAnonymityService = null)
    {
        Account = account;
        _displayName = (accountAnonymityService ?? new AccountAnonymityService())
            .MaskForPresentation(account.DisplayName);
    }

    public HomecomingAccount Account { get; }

    public string StableId => Account.StableId;

    [ObservableProperty]
    private string _displayName;

    public void RefreshDisplayName(AccountAnonymityService accountAnonymityService) =>
        DisplayName = accountAnonymityService.MaskForPresentation(Account.DisplayName);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _statusLabel = "OFFLINE";

    [ObservableProperty]
    private int _characterCount;

    [ObservableProperty]
    private string _activitySummary = string.Empty;

    [ObservableProperty]
    private string _logAvailabilityLabel = string.Empty;

    public string PortraitPath => "Assets/Icons/monitoring/account.svg";
}

public sealed partial class AccountCharacterCardViewModel : ObservableObject
{
    public required string RecordId { get; init; }

    public required string DisplayName { get; init; }

    public string? CharacterShortId { get; init; }

    public string? BuildSaveCommand { get; init; }

    public required string PortraitPath { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowViewingBadge))]
    private bool _isLive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowViewingBadge))]
    private bool _isViewing;

    public bool ShowViewingBadge => IsViewing && !IsLive;
}

public sealed class AccountWorkspaceChipViewModel
{
    public AccountWorkspaceChipViewModel(
        string label,
        string? iconPath,
        bool isActive,
        bool isPlaceholder)
    {
        Label = label;
        IconPath = iconPath;
        IsActive = isActive;
        IsPlaceholder = isPlaceholder;
    }

    public string Label { get; }

    public string? IconPath { get; }

    public bool IsActive { get; }

    public bool IsPlaceholder { get; }

    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath);
}
