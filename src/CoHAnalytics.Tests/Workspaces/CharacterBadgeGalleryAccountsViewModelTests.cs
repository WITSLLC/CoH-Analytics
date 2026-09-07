using System.IO;
using System.Text;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class CharacterBadgeGalleryAccountsViewModelTests
{
    private const string GalleryFixtureJson =
        """
        {
          "manifest": {
            "catalogVersion": "item-ref-badge-gallery-vm-test",
            "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
          },
          "items": [
            {
              "catalogItemId": "BAD-91001",
              "family": "Badge",
              "subtype": "Accolades",
              "currentDisplayName": "Atlas Tour Guide",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "Badge_HeroExploreAccolade"
            },
            {
              "catalogItemId": "BAD-91002",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Alpha Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_tourist_01"
            }
          ],
          "aliases": [],
          "enhancementSets": [],
          "badges": [
            {
              "catalogItemId": "BAD-91001",
              "homecomingSourceId": "AtlasParkExplorer",
              "canonicalCategory": "Accolades",
              "badgeType": 5,
              "referenceKind": "Accolade",
              "heroName": "Atlas Tour Guide",
              "villainName": "Atlas Tour Guide",
              "heroIcon": "Badge_HeroExploreAccolade",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-91002",
              "homecomingSourceId": "AtlasParkTour1",
              "canonicalCategory": "Exploration",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Alpha Badge",
              "villainName": "Alpha Badge",
              "heroIcon": "badge_tourist_01",
              "verificationStatus": "VerifiedMultiSource"
            }
          ],
          "zones": [],
          "badgeLocations": []
        }
        """;

    [Fact]
    public void Selected_character_shows_only_its_acquired_badges()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (characterRepository, badgeRepository) = CreateRepositories();
        var alpha = characterRepository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var beta = characterRepository.EstablishTrustedFromWelcome("acct-a", "Beta Hero");
        badgeRepository.RecordAcquisition(
            alpha.RecordId!,
            "acct-a",
            "BAD-91001",
            "Atlas Tour Guide",
            DateTimeOffset.UtcNow);
        badgeRepository.RecordAcquisition(
            beta.RecordId!,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);

        var (viewModel, _, viewed, _) = CreateViewModel(
            characterRepository,
            badgeRepository,
            catalog);
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(alpha.RecordId!, "acct-a");

        Assert.Equal("1 Badge Earned", viewModel.CharacterBadgeCountLabel);
        Assert.Single(viewModel.CharacterBadgeCategories);
        Assert.Equal("Accolades", viewModel.CharacterBadgeCategories[0].CategoryName);
        Assert.Equal("Atlas Tour Guide", viewModel.CharacterBadgeCategories[0].Badges[0].DisplayName);

        var betaCard = viewModel.AccountCharacters.Single(card => card.DisplayName == "Beta Hero");
        viewModel.SelectedCharacter = betaCard;

        Assert.Equal("1 Badge Earned", viewModel.CharacterBadgeCountLabel);
        Assert.Single(viewModel.CharacterBadgeCategories);
        Assert.Equal("Exploration", viewModel.CharacterBadgeCategories[0].CategoryName);
    }

    [Fact]
    public void Zero_badge_character_shows_empty_state()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (characterRepository, badgeRepository) = CreateRepositories();
        var established = characterRepository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");

        var (viewModel, _, viewed, _) = CreateViewModel(
            characterRepository,
            badgeRepository,
            catalog);
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");
        viewModel.SelectedCharacterTab = AccountCharacterDetailTab.Badges;

        Assert.True(viewModel.ShowCharacterBadgeEmptyState);
        Assert.False(viewModel.ShowCharacterBadgeGallery);
        Assert.Empty(viewModel.CharacterBadgeCategories);
    }

    [Fact]
    public void Acquisition_repository_change_refreshes_gallery_when_gameplay_session_changes()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (characterRepository, badgeRepository) = CreateRepositories();
        var established = characterRepository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var gameplay = new RecordingGameplaySessionManager();

        var (viewModel, _, viewed, _) = CreateViewModel(
            characterRepository,
            badgeRepository,
            catalog,
            gameplay);
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");
        viewModel.SelectedCharacterTab = AccountCharacterDetailTab.Badges;

        Assert.True(viewModel.ShowCharacterBadgeEmptyState);

        badgeRepository.RecordAcquisition(
            established.RecordId!,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);
        gameplay.RaiseStateChanged();

        Assert.False(viewModel.ShowCharacterBadgeEmptyState);
        Assert.Equal("1 Badge Earned", viewModel.CharacterBadgeCountLabel);
        Assert.Equal("Exploration", viewModel.CharacterBadgeCategories[0].CategoryName);
    }

    [Fact]
    public void Gameplay_session_refresh_preserves_gallery_items_when_badges_are_unchanged()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (characterRepository, badgeRepository) = CreateRepositories();
        var established = characterRepository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        badgeRepository.RecordAcquisition(
            established.RecordId!,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);
        var gameplay = new RecordingGameplaySessionManager();

        var (viewModel, _, viewed, _) = CreateViewModel(
            characterRepository,
            badgeRepository,
            catalog,
            gameplay);
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        var selectedCharacter = Assert.IsType<AccountCharacterCardViewModel>(viewModel.SelectedCharacter);
        var characterCard = Assert.Single(viewModel.AccountCharacters);
        var category = Assert.Single(viewModel.CharacterBadgeCategories);
        var badge = Assert.Single(category.Badges);

        gameplay.RaiseStateChanged();

        Assert.Same(selectedCharacter, viewModel.SelectedCharacter);
        Assert.Same(characterCard, Assert.Single(viewModel.AccountCharacters));
        Assert.Same(category, Assert.Single(viewModel.CharacterBadgeCategories));
        Assert.Same(badge, Assert.Single(viewModel.CharacterBadgeCategories[0].Badges));
    }

    [Fact]
    public void Renamed_character_retains_badges_by_stable_record_id()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (characterRepository, badgeRepository) = CreateRepositories();
        var established = characterRepository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        badgeRepository.RecordAcquisition(
            established.RecordId!,
            "acct-a",
            "BAD-91001",
            "Atlas Tour Guide",
            DateTimeOffset.UtcNow);

        var (viewModel, repository, viewed, _) = CreateViewModel(
            characterRepository,
            badgeRepository,
            catalog);
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        repository.RecordTrustedObservedDisplayName(
            established.RecordId!,
            "Renamed Hero",
            CharacterTrustState.TrustedFromWelcome);

        Assert.Equal("Renamed Hero", viewModel.CharacterHeaderName);
        Assert.Equal("1 Badge Earned", viewModel.CharacterBadgeCountLabel);
        Assert.Equal("Atlas Tour Guide", viewModel.CharacterBadgeCategories[0].Badges[0].DisplayName);
    }

    private static (AccountsViewModel ViewModel, CharacterRepository Repository, RecordingViewedContextService Viewed, FakeIdentityReadService Identity)
        CreateViewModel(
            CharacterRepository repository,
            CharacterBadgeAcquisitionRepository badgeRepository,
            IItemReferenceCatalog catalog,
            IGameplaySessionManager? gameplay = null)
    {
        var timeProvider = new ManualTimeProvider();
        var importService = new CharacterBuildImportService(repository, timeProvider: timeProvider);
        var viewed = new RecordingViewedContextService();
        var identity = new FakeIdentityReadService();

        var settings = new SettingsService();
        var installation = new HomecomingInstallationService(settings);
        var accountDiscovery = new HomecomingAccountDiscoveryService(installation);
        var launcher = new HomecomingLauncherService(settings, installation);
        var runtime = new HomecomingRuntimeService(installation, launcher);
        var logActivity = new LogActivityService(accountDiscovery);
        var monitoring = new FakeMonitoringSessionManager();
        gameplay ??= new GameplaySessionManager(
            monitoring,
            new GameplaySessionTestInfrastructure.FakeGameplayParserManager(),
            repository);

        var viewModel = new AccountsViewModel(
            accountDiscovery,
            repository,
            new ApplicationOrchestrator(),
            runtime,
            monitoring,
            gameplay,
            identity,
            viewed,
            logActivity,
            importService,
            accountAnonymityService: null,
            characterBadgeAcquisitionRepository: badgeRepository,
            itemReferenceCatalog: catalog);

        return (viewModel, repository, viewed, identity);
    }

    private static (CharacterRepository CharacterRepository, CharacterBadgeAcquisitionRepository BadgeRepository) CreateRepositories()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-badge-gallery",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        var characterRepository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = dataDirectory
        });
        var badgeRepository = new CharacterBadgeAcquisitionRepository(
            new CharacterBadgeAcquisitionRepositoryOptions { DataDirectory = dataDirectory });
        return (characterRepository, badgeRepository);
    }

    private static IItemReferenceCatalog LoadCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(result.Succeeded, result.FailureReason);
        return ItemReferenceCatalog.FromLoadResult(result);
    }

    private static void SelectAccount(AccountsViewModel viewModel, string stableId, string folderPath)
    {
        var account = new HomecomingAccount
        {
            StableId = stableId,
            DisplayName = stableId,
            FolderName = stableId,
            FolderPath = folderPath,
            HasBuildsFolder = Directory.Exists(Path.Combine(folderPath, "Builds")),
            HasLogsFolder = Directory.Exists(Path.Combine(folderPath, "Logs")),
            Status = HomecomingAccountStatus.Ready
        };

        viewModel.SelectedAccount = new AccountListItemViewModel(account);
    }

    private static string CreateAccountFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coh-analytics-account", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(folder, "Builds"));
        return folder;
    }

    private sealed class RecordingViewedContextService : IViewedContextService
    {
        public ViewedContextState Current { get; private set; } = ViewedContextState.Empty;

        public event EventHandler<ViewedContextChangedEventArgs>? Changed;

        public void SelectViewedAccount(string accountStableId)
        {
            Current = Current with { AccountStableId = accountStableId };
            Changed?.Invoke(this, new ViewedContextChangedEventArgs { State = Current });
        }

        public void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId) =>
            SetCharacter(characterRecordId, accountStableId);

        public void SetCharacter(CharacterRecordId characterRecordId, string accountStableId)
        {
            Current = Current with
            {
                AccountStableId = accountStableId,
                CharacterRecordId = characterRecordId,
                IsFollowingLive = false
            };
            Changed?.Invoke(this, new ViewedContextChangedEventArgs { State = Current });
        }

        public void ReturnToLive() => Current = ViewedContextState.Empty;

        public void SelectGameplaySessionContext(MonitoringContextId contextId)
        {
        }

        public void ResetForNewRuntimeGeneration() => Current = ViewedContextState.Empty;
    }

    private sealed class RecordingGameplaySessionManager : IGameplaySessionManager
    {
        public GameplaySessionManagerSnapshot Current { get; } = GameplaySessionManagerSnapshot.Empty;

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged;

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public GameplaySessionOperationResult ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public void ResetForNewRuntimeGeneration()
        {
        }

        public GameplaySessionDiagnostics GetDiagnostics() =>
            GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(isRunning: false);

        public void RaiseStateChanged() =>
            StateChanged?.Invoke(this, new GameplaySessionManagerChangedEventArgs(Current));
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public CharacterRecordId? LiveRecordId { get; private set; }

        public GameplaySessionIdentityReadModelSnapshot Current { get; private set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

        public void SetLiveCharacter(string accountStableId, CharacterRecordId recordId, string displayName)
        {
            LiveRecordId = recordId;
            Current = GameplaySessionIdentityReadModelSnapshot.Create(
                [
                    new LiveMonitoringContextIdentityReadModel
                    {
                        ContextId = MonitoringContextId.CreateNew(),
                        ContextState = MonitoringContextState.Ready,
                        AccountStableId = accountStableId,
                        CharacterRecordId = recordId,
                        CharacterDisplayName = displayName,
                        CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
                        CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
                        SessionLifecycleState = GameplaySessionLifecycleState.Active,
                        HasActiveSession = true,
                        IdentityStatusLabel = "Confirmed",
                        IdentityDetail = displayName
                    }
                ],
                DateTimeOffset.UtcNow,
                1);
            Changed?.Invoke(this, new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = Current });
        }
    }
}
