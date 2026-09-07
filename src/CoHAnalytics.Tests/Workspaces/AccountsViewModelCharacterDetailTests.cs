using System.IO;
using System.Windows;
using System.Windows.Media;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class AccountsViewModelCharacterDetailTests
{
    [Fact]
    public void Workspace_chips_expose_only_active_characters_for_beta()
    {
        var (viewModel, _, _, _) = CreateViewModel();

        var chip = Assert.Single(viewModel.WorkspaceChips);
        Assert.Equal("Characters", chip.Label);
        Assert.True(chip.IsActive);
        Assert.False(chip.IsPlaceholder);
    }

    [Fact]
    public void Selected_account_exposes_only_its_characters_in_selector()
    {
        var (viewModel, repository, _, _) = CreateViewModel();
        repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        repository.EstablishTrustedFromWelcome("acct-a", "Beta Hero");
        repository.EstablishTrustedFromWelcome("acct-b", "Gamma Hero");

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());

        Assert.Equal(2, viewModel.AccountCharacters.Count);
        Assert.Contains(viewModel.AccountCharacters, card => card.DisplayName == "Alpha Hero");
        Assert.Contains(viewModel.AccountCharacters, card => card.DisplayName == "Beta Hero");
        Assert.DoesNotContain(viewModel.AccountCharacters, card => card.DisplayName == "Gamma Hero");
    }

    [Fact]
    public void Selecting_character_updates_persistent_header()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        repository.RecordObservedLevel(established.RecordId!, 50, DateTimeOffset.UtcNow);
        repository.ImportBuildMetadata(
            established.RecordId!,
            "Fire Control",
            "Kinetics",
            "Controller",
            null,
            DateTimeOffset.UtcNow);

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        Assert.Equal("Alpha Hero", viewModel.CharacterHeaderName);
        Assert.Equal("Level 50", viewModel.CharacterHeaderLevel);
        Assert.Equal("Controller:", viewModel.CharacterHeaderArchetypeLabel);
        Assert.Equal("Fire Control / Kinetics", viewModel.CharacterHeaderPowersetPair);
        Assert.Equal("Imported", viewModel.CharacterHeaderBuildValue);
    }

    [Fact]
    public void Unknown_powerset_metadata_does_not_show_placeholder_line()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        Assert.False(viewModel.ShowCharacterHeaderPowersetLine);
        Assert.Null(viewModel.CharacterHeaderArchetypeLabel);
        Assert.Null(viewModel.CharacterHeaderPowersetPair);
        Assert.Equal("Not imported", viewModel.CharacterHeaderBuildValue);
    }

    [Fact]
    public void Last_played_displays_in_header()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        repository.RecordTrustedActivity(established.RecordId!, DateTimeOffset.UtcNow);

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        Assert.NotEmpty(viewModel.CharacterHeaderLastPlayedValue);
        Assert.NotEqual("Not yet observed", viewModel.CharacterHeaderLastPlayedValue);
    }

    [Fact]
    public void Buildsave_command_matches_selected_character_short_id()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        Assert.Equal($"/buildsavefile {shortId}.txt", viewModel.SelectedCharacterBuildSaveCommand);
    }

    [Fact]
    public void Copy_copies_exact_buildsave_command()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var (viewModel, repository, viewed, _) = CreateViewModel();
                var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
                var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;

                SelectAccount(viewModel, "acct-a", CreateAccountFolder());
                viewed.SetCharacter(established.RecordId!, "acct-a");

                viewModel.CopyBuildSaveCommandCommand.Execute(null);

                Assert.Equal($"/buildsavefile {shortId}.txt", Clipboard.GetText());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Copy buildsave command test timed out.");
        if (failure is not null)
        {
            throw failure;
        }
    }

    [Fact]
    public void Refresh_build_metadata_imports_without_restart()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel();
        var accountFolder = CreateAccountFolder();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;
        WriteSampleBuild(Path.Combine(accountFolder, "Builds", $"{shortId}.txt"));

        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(established.RecordId!, "acct-a");

        viewModel.RefreshBuildMetadataCommand.Execute(null);

        Assert.True(viewModel.HasImportedBuildMetadata);
        Assert.Equal("Brute:", viewModel.CharacterHeaderArchetypeLabel);
        Assert.Equal("Fighting / Armor", viewModel.CharacterHeaderPowersetPair);
    }

    [Fact]
    public void Refresh_build_metadata_reports_missing_file_quietly()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        viewModel.RefreshBuildMetadataCommand.Execute(null);

        Assert.Equal("No buildsave file found yet.", viewModel.BuildImportStatusMessage);
    }

    [Fact]
    public void Changing_viewed_character_does_not_change_live_identity_read_model()
    {
        var (viewModel, repository, viewed, identity) = CreateViewModel();
        var first = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var second = repository.EstablishTrustedFromWelcome("acct-a", "Beta Hero");
        identity.SetLiveCharacter("acct-a", first.RecordId!, "Alpha Hero");

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(second.RecordId!, "acct-a");

        Assert.Equal(first.RecordId, identity.LiveRecordId);
    }

    [Fact]
    public void Only_overview_and_badges_tabs_are_available()
    {
        var (viewModel, _, _, _) = CreateViewModel();

        Assert.Equal(AccountCharacterDetailTab.Overview, viewModel.SelectedCharacterTab);
        Assert.True(viewModel.ShowOverviewTabContent);
        Assert.False(viewModel.ShowBadgesTabContent);

        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Badges);

        Assert.Equal(AccountCharacterDetailTab.Badges, viewModel.SelectedCharacterTab);
        Assert.True(viewModel.ShowBadgesTabContent);
        Assert.False(viewModel.ShowOverviewTabContent);
    }

    [Fact]
    public void Character_icon_selection_replaces_placeholder_and_is_character_specific()
    {
        var iconService = new FakeBuiltInCharacterIconService(
            "default-male-01",
            "default-female-01");
        var (viewModel, repository, viewed, _) = CreateViewModel(iconService);
        var alpha = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var beta = repository.EstablishTrustedFromWelcome("acct-a", "Beta Hero");
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(alpha.RecordId!, "acct-a");

        Assert.True(viewModel.ShowCharacterHeaderIconPlaceholder);
        var picker = viewModel.CreateCharacterIconPicker();
        Assert.NotNull(picker);
        picker!.SelectedIcon = picker.Icons[0];

        Assert.True(viewModel.ApplyCharacterIconSelection(picker.SelectedIcon.IconId));
        Assert.True(viewModel.ShowCharacterHeaderIcon);
        Assert.Equal("default-male-01", viewModel.CharacterHeaderIconId);
        Assert.Same(iconService.Icons[0].ImageSource, viewModel.CharacterHeaderIconSource);

        viewed.SetCharacter(beta.RecordId!, "acct-a");
        Assert.True(viewModel.ShowCharacterHeaderIconPlaceholder);
        Assert.Null(viewModel.CharacterHeaderIconId);

        viewed.SetCharacter(alpha.RecordId!, "acct-a");
        Assert.Equal("default-male-01", viewModel.CharacterHeaderIconId);
    }

    [Fact]
    public void Opening_picker_without_apply_does_not_persist_selection()
    {
        var iconService = new FakeBuiltInCharacterIconService("default-male-01");
        var (viewModel, repository, viewed, _) = CreateViewModel(iconService);
        var character = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(character.RecordId!, "acct-a");

        var picker = viewModel.CreateCharacterIconPicker();
        picker!.SelectedIcon = picker.Icons[0];

        Assert.Null(repository.TryGetRecord(character.RecordId!)!.IconReference);
        Assert.True(viewModel.ShowCharacterHeaderIconPlaceholder);
    }

    [Fact]
    public void Custom_icon_assignment_is_character_specific_and_resolves_after_switching()
    {
        var custom = new CustomCharacterIcon
        {
            Id = "custom-" + new string('d', 64),
            ImageSource = new DrawingImage()
        };
        var customService = new FakeCustomCharacterIconService(custom);
        var builtInService = new FakeBuiltInCharacterIconService("default-male-01");
        var (viewModel, repository, viewed, _) = CreateViewModel(builtInService, customService);
        var alpha = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var beta = repository.EstablishTrustedFromWelcome("acct-a", "Beta Hero");
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(alpha.RecordId!, "acct-a");

        Assert.True(viewModel.ApplyCharacterIconSelection(
            CharacterIconReference.Custom(custom.Id)));
        Assert.Equal(custom.Id, viewModel.CharacterHeaderIconId);
        Assert.Same(custom.ImageSource, viewModel.CharacterHeaderIconSource);

        viewed.SetCharacter(beta.RecordId!, "acct-a");
        Assert.True(viewModel.ShowCharacterHeaderIconPlaceholder);
        viewed.SetCharacter(alpha.RecordId!, "acct-a");
        Assert.Same(custom.ImageSource, viewModel.CharacterHeaderIconSource);

        var picker = viewModel.CreateCharacterIconPicker();
        Assert.Equal(CharacterIconKind.Custom, picker!.SelectedIcon!.Reference.Kind);
        Assert.Equal(custom.Id, picker.SelectedIcon.IconId);
        Assert.Equal(["Alpha Hero"], picker.GetSelectedCustomIconAssignments());
    }

    [Fact]
    public void Unresolved_custom_icon_cannot_replace_the_existing_assignment()
    {
        var builtInService = new FakeBuiltInCharacterIconService("default-male-01");
        var (viewModel, repository, viewed, _) = CreateViewModel(
            builtInService,
            new FakeCustomCharacterIconService());
        var character = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(character.RecordId!, "acct-a");
        Assert.True(viewModel.ApplyCharacterIconSelection("default-male-01"));

        var missing = CharacterIconReference.Custom("custom-" + new string('e', 64));
        Assert.False(viewModel.ApplyCharacterIconSelection(missing));

        var persisted = repository.TryGetRecord(character.RecordId!)!.IconReference;
        Assert.Equal(CharacterIconKind.BuiltIn, persisted!.Kind);
        Assert.Equal("default-male-01", persisted.IconId);
    }

    private static (AccountsViewModel ViewModel, CharacterRepository Repository, RecordingViewedContextService Viewed, FakeIdentityReadService Identity) CreateViewModel(
        IBuiltInCharacterIconService? iconService = null,
        ICustomCharacterIconService? customIconService = null)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-accounts-vm", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        var timeProvider = new ManualTimeProvider();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = dataDirectory,
            TimeProvider = timeProvider
        });
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
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var gameplay = new GameplaySessionManager(monitoring, parser, repository);

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
            builtInCharacterIconService: iconService,
            customCharacterIconService: customIconService);

        return (viewModel, repository, viewed, identity);
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

    private static void WriteSampleBuild(string path)
    {
        const string content = """
            Alpha Hero: Level 50 Magic Class_brute
            header
            header
            header
            Level 1: brute_fighting Punch
            Level 2: brute_armor Enrage
            """;
        File.WriteAllText(path, content);
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

    private sealed class FakeBuiltInCharacterIconService(params string[] iconIds)
        : IBuiltInCharacterIconService
    {
        public IReadOnlyList<BuiltInCharacterIcon> Icons { get; } = iconIds
            .Select(iconId => new BuiltInCharacterIcon
            {
                Id = iconId,
                ImageSource = new DrawingImage()
            })
            .ToArray();

        public bool TryGetIcon(string iconId, out BuiltInCharacterIcon icon)
        {
            icon = Icons.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, iconId, StringComparison.Ordinal))!;
            return icon is not null;
        }
    }

    private sealed class FakeCustomCharacterIconService(params CustomCharacterIcon[] icons)
        : ICustomCharacterIconService
    {
        public IReadOnlyList<CustomCharacterIcon> GetIcons() => icons;

        public bool TryGetIcon(string iconId, out CustomCharacterIcon icon)
        {
            icon = icons.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, iconId, StringComparison.Ordinal))!;
            return icon is not null;
        }

        public CustomCharacterIconSaveResult SaveNormalizedIcon(byte[] pngData) =>
            throw new NotSupportedException();

        public CustomCharacterIconDeleteResult DeleteIcon(string iconId) =>
            CustomCharacterIconDeleteResult.Success();
    }
}
