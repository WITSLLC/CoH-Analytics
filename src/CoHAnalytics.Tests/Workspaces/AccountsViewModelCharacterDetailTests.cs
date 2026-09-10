using System.IO;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;
using CoHAnalytics.ReferenceData;

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
    public void Copy_sends_exact_buildsave_command_to_clipboard_once()
    {
        var clipboard = new FakeClipboardService();
        var (viewModel, repository, viewed, _) = CreateViewModel(clipboardService: clipboard);
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        viewModel.CopyBuildSaveCommandCommand.Execute(null);

        Assert.Equal(1, clipboard.CallCount);
        Assert.Equal($"/buildsavefile {shortId}.txt", clipboard.LastText);
        Assert.True(viewModel.ShowBuildSaveCopiedFeedback);
    }

    [Fact]
    public void Copy_does_not_invoke_clipboard_when_buildsave_command_missing()
    {
        var clipboard = new FakeClipboardService();
        var (viewModel, _, _, _) = CreateViewModel(clipboardService: clipboard);

        viewModel.CopyBuildSaveCommandCommand.Execute(null);

        Assert.Equal(0, clipboard.CallCount);
        Assert.False(viewModel.ShowBuildSaveCopiedFeedback);
    }

    [Fact]
    public void Copy_failure_does_not_show_copied_feedback_or_throw()
    {
        var clipboard = new FakeClipboardService();
        clipboard.EnqueueResult(false);
        var (viewModel, repository, viewed, _) = CreateViewModel(clipboardService: clipboard);
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        var exception = Record.Exception(() => viewModel.CopyBuildSaveCommandCommand.Execute(null));

        Assert.Null(exception);
        Assert.Equal(1, clipboard.CallCount);
        Assert.False(viewModel.ShowBuildSaveCopiedFeedback);
    }

    [Fact]
    public void Repeated_successful_copy_keeps_feedback_visible()
    {
        var clipboard = new FakeClipboardService();
        var (viewModel, repository, viewed, _) = CreateViewModel(clipboardService: clipboard);
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;

        SelectAccount(viewModel, "acct-a", CreateAccountFolder());
        viewed.SetCharacter(established.RecordId!, "acct-a");

        viewModel.CopyBuildSaveCommandCommand.Execute(null);
        Assert.True(viewModel.ShowBuildSaveCopiedFeedback);

        viewModel.CopyBuildSaveCommandCommand.Execute(null);

        Assert.Equal(2, clipboard.CallCount);
        Assert.All(clipboard.Texts, text => Assert.Equal($"/buildsavefile {shortId}.txt", text));
        Assert.True(viewModel.ShowBuildSaveCopiedFeedback);
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
    public void Overview_badges_and_build_tabs_are_available_without_changing_existing_tabs()
    {
        var (viewModel, _, _, _) = CreateViewModel();

        Assert.Equal(AccountCharacterDetailTab.Overview, viewModel.SelectedCharacterTab);
        Assert.True(viewModel.ShowOverviewTabContent);
        Assert.False(viewModel.ShowBadgesTabContent);

        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Badges);

        Assert.Equal(AccountCharacterDetailTab.Badges, viewModel.SelectedCharacterTab);
        Assert.True(viewModel.ShowBadgesTabContent);
        Assert.False(viewModel.ShowOverviewTabContent);

        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Build);

        Assert.Equal(AccountCharacterDetailTab.Build, viewModel.SelectedCharacterTab);
        Assert.True(viewModel.ShowBuildTabContent);
        Assert.False(viewModel.ShowOverviewTabContent);
        Assert.False(viewModel.ShowBadgesTabContent);
    }

    [Fact]
    public void Build_sync_loads_selected_character_and_retains_the_last_successful_presentation()
    {
        var assetProvider = new RecordingInstalledGameAssetProvider();
        var compositor = new RecordingEnhancementIconCompositor();
        var powerCatalog = new FakeBuildPowerCatalog();
        var itemCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var (viewModel, repository, viewed, _) = CreateViewModel(
            itemReferenceCatalog: itemCatalog,
            installedGameAssetProvider: assetProvider,
            powerReferenceCatalog: powerCatalog,
            enhancementIconCompositor: compositor);
        var accountFolder = CreateAccountFolder();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        repository.ImportBuildMetadata(
            established.RecordId!,
            "Fiery Melee",
            "Fiery Aura",
            "Brute",
            null,
            DateTimeOffset.UtcNow);
        var record = repository.TryGetRecord(established.RecordId!)!;
        WriteBuildLayout(Path.Combine(accountFolder, "Builds", $"{record.CharacterShortId}.txt"));
        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(established.RecordId!, "acct-a");

        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Build);
        viewModel.SyncBuildFromBuildCommand.Execute(null);

        Assert.True(viewModel.HasBuildPresentation);
        Assert.False(viewModel.ShowBuildEmptyState);
        Assert.Equal("Fiery Melee", viewModel.BuildPrimarySection!.DisplayName);
        Assert.Equal("Scorch", Assert.Single(viewModel.BuildPrimarySection.Powers).DisplayName);
        Assert.Equal("Fiery Aura", viewModel.BuildSecondarySection!.DisplayName);
        Assert.Equal("Leaping", Assert.Single(viewModel.BuildAdditionalSections).DisplayName);
        Assert.Equal("Build synced from Homecoming.", viewModel.BuildSyncStatusMessage);
        Assert.Contains("power_scorch.tga", assetProvider.Identities);
        Assert.Contains(EnhancementIconIdentity.EmptySlot, assetProvider.Identities);
        Assert.Single(compositor.Requests);

        var primary = viewModel.BuildPrimarySection;
        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Overview);
        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Build);

        Assert.Same(primary, viewModel.BuildPrimarySection);
        Assert.True(viewModel.ShowBuildContent);
    }

    [Fact]
    public void Persisted_build_loads_in_a_new_viewmodel_when_the_source_file_is_missing()
    {
        var dataDirectory = CreateDataDirectory();
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = dataDirectory
        });
        var store = new CharacterBuildSnapshotStore(new CharacterBuildSnapshotStoreOptions
        {
            DataDirectory = dataDirectory
        });
        var itemCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var powerCatalog = new FakeBuildPowerCatalog();
        var assetProvider = new RecordingInstalledGameAssetProvider();
        var compositor = new RecordingEnhancementIconCompositor();
        var (viewModel, _, viewed, _) = CreateViewModel(
            itemReferenceCatalog: itemCatalog,
            installedGameAssetProvider: assetProvider,
            powerReferenceCatalog: powerCatalog,
            enhancementIconCompositor: compositor,
            characterRepository: repository,
            characterBuildSnapshotStore: store);
        var accountFolder = CreateAccountFolder();
        var selected = EstablishCharacter(
            repository,
            "Blue Devil",
            38,
            "Fiery Melee",
            "Fiery Aura",
            "Brute",
            "default-male-02");
        var buildPath = Path.Combine(accountFolder, "Builds", $"{selected.CharacterShortId}.txt");
        WriteBuildLayout(buildPath);
        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(selected.RecordId, "acct-a");
        viewModel.SyncBuildFromBuildCommand.Execute(null);
        var freshAnalysis = viewModel.CreateBuildSetAnalysis();
        var identityBeforeRestart = repository.TryGetRecord(selected.RecordId)!;
        viewModel.Dispose();
        File.Delete(buildPath);

        var recreatedAssetProvider = new RecordingInstalledGameAssetProvider();
        var recreatedCompositor = new RecordingEnhancementIconCompositor();
        var (recreated, _, recreatedViewed, _) = CreateViewModel(
            itemReferenceCatalog: itemCatalog,
            installedGameAssetProvider: recreatedAssetProvider,
            powerReferenceCatalog: powerCatalog,
            enhancementIconCompositor: recreatedCompositor,
            characterRepository: repository,
            characterBuildSnapshotStore: store);
        SelectAccount(recreated, "acct-a", accountFolder);
        recreatedViewed.SetCharacter(selected.RecordId, "acct-a");

        Assert.True(recreated.HasBuildPresentation);
        Assert.Equal("Fiery Melee", recreated.BuildPrimarySection!.DisplayName);
        Assert.Equal("Scorch", Assert.Single(recreated.BuildPrimarySection.Powers).DisplayName);
        Assert.Equal("Fiery Aura", recreated.BuildSecondarySection!.DisplayName);
        Assert.Equal("Leaping", Assert.Single(recreated.BuildAdditionalSections).DisplayName);
        Assert.Contains("power_scorch.tga", recreatedAssetProvider.Identities);
        Assert.Contains(EnhancementIconIdentity.EmptySlot, recreatedAssetProvider.Identities);
        Assert.Single(recreatedCompositor.Requests);
        var persistedAnalysis = recreated.CreateBuildSetAnalysis();
        Assert.NotNull(freshAnalysis);
        Assert.NotNull(persistedAnalysis);
        Assert.Equal(freshAnalysis.SummaryBonuses, persistedAnalysis.SummaryBonuses);
        Assert.Equal(freshAnalysis.GlobalBonuses, persistedAnalysis.GlobalBonuses);
        Assert.Equal(
            freshAnalysis.Sets.Select(set => (set.EnhancementSetId, set.PieceCount)),
            persistedAnalysis.Sets.Select(set => (set.EnhancementSetId, set.PieceCount)));
        AssertIdentityUnchanged(repository, identityBeforeRestart);
    }

    [Fact]
    public void Persisted_header_metadata_mismatch_never_changes_character_identity_or_icon()
    {
        var dataDirectory = CreateDataDirectory();
        var store = new CharacterBuildSnapshotStore(new CharacterBuildSnapshotStoreOptions
        {
            DataDirectory = dataDirectory
        });
        var (viewModel, repository, viewed, _) = CreateViewModel(
            itemReferenceCatalog: ItemReferenceCatalogFactory.LoadEmbeddedProduction(),
            installedGameAssetProvider: new RecordingInstalledGameAssetProvider(),
            powerReferenceCatalog: new FakeBuildPowerCatalog(),
            enhancementIconCompositor: new RecordingEnhancementIconCompositor(),
            characterBuildSnapshotStore: store);
        var selected = EstablishCharacter(
            repository,
            "Blue Devil",
            38,
            "Fiery Melee",
            "Fiery Aura",
            "Brute",
            "default-male-02");
        var mismatchedLayout = new HomecomingBuildLayoutSnapshot(
            "Hell's Vengence",
            50,
            "Class_Brute",
            [
                new HomecomingBuildPowerSnapshot(
                    1,
                    "Brute_Melee",
                    "Fiery_Melee",
                    "Scorch",
                    0,
                    [new HomecomingBuildSlotSnapshot(true, null, false, null, null, 0)])
            ]);
        Assert.True(store.Save(new CharacterBuildSnapshot
        {
            CharacterRecordId = selected.RecordId,
            CharacterShortId = "a-different-informational-id",
            CharacterName = "Hell's Vengence",
            SyncedAtUtc = DateTimeOffset.UtcNow,
            Layout = mismatchedLayout
        }).IsSuccess);
        var accountFolder = CreateAccountFolder();

        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(selected.RecordId, "acct-a");

        Assert.True(viewModel.HasBuildPresentation);
        Assert.Equal("Scorch", Assert.Single(viewModel.BuildPrimarySection!.Powers).DisplayName);
        AssertIdentityUnchanged(repository, selected);
    }

    [Fact]
    public void Failed_refresh_and_damaged_persisted_file_do_not_replace_the_session_cache()
    {
        var dataDirectory = CreateDataDirectory();
        var store = new CharacterBuildSnapshotStore(new CharacterBuildSnapshotStoreOptions
        {
            DataDirectory = dataDirectory
        });
        var (viewModel, repository, viewed, _) = CreateViewModel(
            itemReferenceCatalog: ItemReferenceCatalogFactory.LoadEmbeddedProduction(),
            installedGameAssetProvider: new RecordingInstalledGameAssetProvider(),
            powerReferenceCatalog: new FakeBuildPowerCatalog(),
            enhancementIconCompositor: new RecordingEnhancementIconCompositor(),
            characterBuildSnapshotStore: store);
        var accountFolder = CreateAccountFolder();
        var selected = EstablishCharacter(
            repository,
            "Blue Devil",
            38,
            "Fiery Melee",
            "Fiery Aura",
            "Brute",
            "default-male-02");
        var buildPath = Path.Combine(accountFolder, "Builds", $"{selected.CharacterShortId}.txt");
        WriteBuildLayout(buildPath);
        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(selected.RecordId, "acct-a");
        viewModel.SyncBuildFromBuildCommand.Execute(null);
        var goodSnapshotJson = File.ReadAllText(store.GetSnapshotPath(selected.RecordId));
        File.WriteAllText(buildPath, "malformed build");

        viewModel.SyncBuildFromBuildCommand.Execute(null);

        Assert.True(viewModel.HasBuildPresentation);
        Assert.Equal("Scorch", Assert.Single(viewModel.BuildPrimarySection!.Powers).DisplayName);
        Assert.Equal(goodSnapshotJson, File.ReadAllText(store.GetSnapshotPath(selected.RecordId)));
        File.WriteAllText(store.GetSnapshotPath(selected.RecordId), "{corrupt");
        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Overview);
        viewModel.SelectCharacterTabCommand.Execute(AccountCharacterDetailTab.Build);
        Assert.Equal("Scorch", Assert.Single(viewModel.BuildPrimarySection!.Powers).DisplayName);
        AssertIdentityUnchanged(repository, selected);
    }

    [Fact]
    public void Build_sync_across_three_characters_preserves_repository_identity_and_caches_per_record()
    {
        var snapshotDataDirectory = CreateDataDirectory();
        var snapshotStore = new CharacterBuildSnapshotStore(new CharacterBuildSnapshotStoreOptions
        {
            DataDirectory = snapshotDataDirectory
        });
        var powerCatalog = new FakeBuildPowerCatalog();
        var (viewModel, repository, viewed, _) = CreateViewModel(
            itemReferenceCatalog: ItemReferenceCatalogFactory.LoadEmbeddedProduction(),
            installedGameAssetProvider: new RecordingInstalledGameAssetProvider(),
            powerReferenceCatalog: powerCatalog,
            enhancementIconCompositor: new RecordingEnhancementIconCompositor(),
            characterBuildSnapshotStore: snapshotStore);
        var accountFolder = CreateAccountFolder();
        var logsFolder = Directory.CreateDirectory(Path.Combine(accountFolder, "Logs")).FullName;
        File.WriteAllText(
            Path.Combine(logsFolder, "chatlog 2026-09-10.txt"),
            "2026-09-10 12:00:00 Welcome to City of Heroes, Hell's Vengence!\r\n");

        var hell = EstablishCharacter(
            repository,
            "Hell's Vengence",
            50,
            "Fire Control",
            "Kinetics",
            "Controller",
            "default-male-01");
        var blue = EstablishCharacter(
            repository,
            "Blue Devil",
            38,
            "Fiery Melee",
            "Fiery Aura",
            "Brute",
            "default-male-02");
        var gerald = EstablishCharacter(
            repository,
            "Gerald Tarrent",
            29,
            "Robotics",
            "Kinetics",
            "Mastermind",
            "default-male-03");

        WriteBuildLayout(
            Path.Combine(accountFolder, "Builds", $"{hell.CharacterShortId}.txt"),
            "Hell's Vengence",
            50,
            "Class_Controller",
            "Controller_Control",
            "Fire_Control",
            "Ring_of_Fire");
        WriteBuildLayout(
            Path.Combine(accountFolder, "Builds", $"{blue.CharacterShortId}.txt"),
            "Blue Devil",
            38,
            "Class_Brute",
            "Brute_Melee",
            "Fiery_Melee",
            "Scorch");
        WriteBuildLayout(
            Path.Combine(accountFolder, "Builds", $"{gerald.CharacterShortId}.txt"),
            "Gerald Tarrent",
            29,
            "Class_Mastermind",
            "Mastermind_Summon",
            "Robotics",
            "Battle_Drones");

        SelectAccount(viewModel, "acct-a", accountFolder);
        var expectedPowers = new[]
        {
            (Record: hell, PowerName: "Ring of Fire"),
            (Record: blue, PowerName: "Scorch"),
            (Record: gerald, PowerName: "Battle Drones")
        };
        foreach (var expected in expectedPowers)
        {
            viewed.SetCharacter(expected.Record.RecordId, "acct-a");
            viewModel.SyncBuildFromBuildCommand.Execute(null);

            Assert.Equal(expected.Record.RecordId.ToString(), viewModel.SelectedCharacter!.RecordId);
            Assert.Equal(expected.PowerName, Assert.Single(viewModel.BuildPrimarySection!.Powers).DisplayName);
        }

        Assert.Equal(3, repository.Current.Records.Count(record => record.AccountStableId == "acct-a"));
        AssertIdentityUnchanged(repository, hell);
        AssertIdentityUnchanged(repository, blue);
        AssertIdentityUnchanged(repository, gerald);

        foreach (var expected in expectedPowers)
        {
            viewed.SetCharacter(expected.Record.RecordId, "acct-a");
            Assert.Equal(expected.PowerName, Assert.Single(viewModel.BuildPrimarySection!.Powers).DisplayName);
        }

        Assert.Equal(3, Directory.EnumerateFiles(
            ApplicationDataPaths.GetBuildSnapshotsDirectory(snapshotDataDirectory),
            "*.json").Count());
        viewModel.Dispose();
        var (recreated, _, recreatedViewed, _) = CreateViewModel(
            itemReferenceCatalog: ItemReferenceCatalogFactory.LoadEmbeddedProduction(),
            installedGameAssetProvider: new RecordingInstalledGameAssetProvider(),
            powerReferenceCatalog: powerCatalog,
            enhancementIconCompositor: new RecordingEnhancementIconCompositor(),
            characterRepository: repository,
            characterBuildSnapshotStore: snapshotStore);
        SelectAccount(recreated, "acct-a", accountFolder);
        foreach (var expected in expectedPowers)
        {
            recreatedViewed.SetCharacter(expected.Record.RecordId, "acct-a");
            Assert.Equal(expected.PowerName, Assert.Single(recreated.BuildPrimarySection!.Powers).DisplayName);
        }

        AssertIdentityUnchanged(repository, hell);
        AssertIdentityUnchanged(repository, blue);
        AssertIdentityUnchanged(repository, gerald);
    }

    [Fact]
    public void Build_sync_reports_mismatched_header_without_changing_selected_character_identity()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel(
            itemReferenceCatalog: ItemReferenceCatalogFactory.LoadEmbeddedProduction(),
            installedGameAssetProvider: new RecordingInstalledGameAssetProvider(),
            powerReferenceCatalog: new FakeBuildPowerCatalog(),
            enhancementIconCompositor: new RecordingEnhancementIconCompositor());
        var accountFolder = CreateAccountFolder();
        var selected = EstablishCharacter(
            repository,
            "Blue Devil",
            38,
            "Fiery Melee",
            "Fiery Aura",
            "Brute",
            "default-male-02");
        WriteBuildLayout(
            Path.Combine(accountFolder, "Builds", $"{selected.CharacterShortId}.txt"),
            "Hell's Vengence",
            50,
            "Class_Brute",
            "Brute_Melee",
            "Fiery_Melee",
            "Scorch");
        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(selected.RecordId, "acct-a");

        viewModel.SyncBuildFromBuildCommand.Execute(null);

        Assert.True(viewModel.HasBuildPresentation);
        Assert.Equal(
            "Build synced; header reports name 'Hell's Vengence' and level 50. Character identity was not changed.",
            viewModel.BuildSyncStatusMessage);
        AssertIdentityUnchanged(repository, selected);
    }

    [Fact]
    public void Malformed_then_repeated_valid_build_sync_is_identity_safe_and_idempotent()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel(
            itemReferenceCatalog: ItemReferenceCatalogFactory.LoadEmbeddedProduction(),
            installedGameAssetProvider: new RecordingInstalledGameAssetProvider(),
            powerReferenceCatalog: new FakeBuildPowerCatalog(),
            enhancementIconCompositor: new RecordingEnhancementIconCompositor());
        var accountFolder = CreateAccountFolder();
        var selected = EstablishCharacter(
            repository,
            "Blue Devil",
            38,
            "Fiery Melee",
            "Fiery Aura",
            "Brute",
            "default-male-02");
        var buildPath = Path.Combine(accountFolder, "Builds", $"{selected.CharacterShortId}.txt");
        File.WriteAllText(buildPath, "not a Homecoming buildsave");
        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(selected.RecordId, "acct-a");

        viewModel.SyncBuildFromBuildCommand.Execute(null);

        Assert.False(viewModel.HasBuildPresentation);
        AssertIdentityUnchanged(repository, selected);

        WriteBuildLayout(
            buildPath,
            "Blue Devil",
            38,
            "Class_Brute",
            "Brute_Melee",
            "Fiery_Melee",
            "Scorch");
        viewModel.SyncBuildFromBuildCommand.Execute(null);
        var firstPresentation = viewModel.BuildPrimarySection;
        viewModel.SyncBuildFromBuildCommand.Execute(null);

        Assert.True(viewModel.HasBuildPresentation);
        Assert.Equal("Scorch", Assert.Single(viewModel.BuildPrimarySection!.Powers).DisplayName);
        Assert.NotSame(firstPresentation, viewModel.BuildPrimarySection);
        AssertIdentityUnchanged(repository, selected);
    }

    [Fact]
    public void Build_sync_failure_is_graceful_and_preserves_existing_accounts_content()
    {
        var (viewModel, repository, viewed, _) = CreateViewModel();
        var accountFolder = CreateAccountFolder();
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        SelectAccount(viewModel, "acct-a", accountFolder);
        viewed.SetCharacter(established.RecordId!, "acct-a");

        viewModel.SyncBuildFromBuildCommand.Execute(null);

        Assert.Equal("No buildsave file found yet.", viewModel.BuildSyncStatusMessage);
        Assert.True(viewModel.ShowBuildEmptyState);
        Assert.Equal("Alpha Hero", viewModel.CharacterHeaderName);
        Assert.NotNull(viewModel.SelectedAccountDetails);
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
        ICustomCharacterIconService? customIconService = null,
        IClipboardService? clipboardService = null,
        IItemReferenceCatalog? itemReferenceCatalog = null,
        IInstalledGameAssetProvider? installedGameAssetProvider = null,
        IHomecomingPowerReferenceCatalog? powerReferenceCatalog = null,
        IEnhancementIconCompositor? enhancementIconCompositor = null,
        CharacterRepository? characterRepository = null,
        ICharacterBuildSnapshotStore? characterBuildSnapshotStore = null)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-accounts-vm", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        var timeProvider = new ManualTimeProvider();
        var repository = characterRepository ?? new CharacterRepository(new CharacterRepositoryOptions
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
            itemReferenceCatalog: itemReferenceCatalog,
            installedGameAssetProvider: installedGameAssetProvider,
            builtInCharacterIconService: iconService,
            customCharacterIconService: customIconService,
            clipboardService: clipboardService,
            powerReferenceCatalog: powerReferenceCatalog,
            enhancementIconCompositor: enhancementIconCompositor,
            characterBuildSnapshotStore: characterBuildSnapshotStore);

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

    private static string CreateDataDirectory()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-accounts-vm-data",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(folder);
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

    private static void WriteBuildLayout(string path)
    {
        const string content = """
            Alpha Hero: Level 38 Magic Class_Brute
            Level 1: Brute_Melee Fiery_Melee Scorch
                Crafted_Damage (35)
                EMPTY
            Level 1: Brute_Defense Fiery_Aura Blazing_Aura
                EMPTY
            Level 8: Pool Leaping Long_Jump
                EMPTY
            """;
        File.WriteAllText(path, content);
    }

    private static void WriteBuildLayout(
        string path,
        string characterName,
        int level,
        string archetype,
        string category,
        string powerset,
        string power)
    {
        var content = $"""
            {characterName}: Level {level} Magic {archetype}
            Level 1: {category} {powerset} {power}
                Crafted_Damage (35)
                EMPTY
            """;
        File.WriteAllText(path, content);
    }

    private static CharacterRecord EstablishCharacter(
        CharacterRepository repository,
        string name,
        int level,
        string primaryPowerSet,
        string secondaryPowerSet,
        string archetype,
        string iconId)
    {
        var established = repository.EstablishTrustedFromWelcome("acct-a", name);
        Assert.True(established.IsSuccess);
        var recordId = established.RecordId!;
        Assert.True(repository.RecordObservedLevel(recordId, level, DateTimeOffset.UtcNow).IsSuccess);
        Assert.True(repository.ImportBuildMetadata(
            recordId,
            primaryPowerSet,
            secondaryPowerSet,
            archetype,
            null,
            DateTimeOffset.UtcNow).IsSuccess);
        Assert.True(repository.SetIconReference(recordId, CharacterIconReference.BuiltIn(iconId)).IsSuccess);
        return repository.TryGetRecord(recordId)!;
    }

    private static void AssertIdentityUnchanged(CharacterRepository repository, CharacterRecord expected)
    {
        var actual = repository.TryGetRecord(expected.RecordId);
        Assert.NotNull(actual);
        Assert.Equal(expected.RecordId, repository.ResolveCanonicalRecordId(expected.RecordId));
        Assert.Equal(expected.AccountStableId, actual.AccountStableId);
        Assert.Equal(expected.CharacterShortId, actual.CharacterShortId);
        Assert.Equal(expected.CurrentDisplayName, actual.CurrentDisplayName);
        Assert.Equal(expected.NormalizedCharacterName, actual.NormalizedCharacterName);
        Assert.Equal(expected.ObservedLevel, actual.ObservedLevel);
        Assert.Equal(expected.Archetype, actual.Archetype);
        Assert.Equal(expected.PrimaryPowerSet, actual.PrimaryPowerSet);
        Assert.Equal(expected.SecondaryPowerSet, actual.SecondaryPowerSet);
        Assert.Equal(expected.IconReference, actual.IconReference);
        Assert.Equal(expected.Aliases, actual.Aliases);
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

    private sealed class FakeBuildPowerCatalog : IHomecomingPowerReferenceCatalog
    {
        public bool IsLoaded => true;

        public bool TryResolve(
            string categoryId,
            string powersetId,
            string powerId,
            out HomecomingPowerReference power)
        {
            power = (categoryId, powersetId, powerId) switch
            {
                ("Brute_Melee", "Fiery_Melee", "Scorch") => Create(
                    categoryId, powersetId, powerId, "Fiery Melee", "Scorch", "power_scorch.tga"),
                ("Controller_Control", "Fire_Control", "Ring_of_Fire") => Create(
                    categoryId, powersetId, powerId, "Fire Control", "Ring of Fire", "power_ring_fire.tga"),
                ("Mastermind_Summon", "Robotics", "Battle_Drones") => Create(
                    categoryId, powersetId, powerId, "Robotics", "Battle Drones", "power_battle_drones.tga"),
                ("Brute_Defense", "Fiery_Aura", "Blazing_Aura") => Create(
                    categoryId, powersetId, powerId, "Fiery Aura", "Blazing Aura", "power_aura.tga"),
                ("Pool", "Leaping", "Long_Jump") => Create(
                    categoryId, powersetId, powerId, "Leaping", "Super Jump", "power_jump.tga"),
                _ => default
            };
            return !string.IsNullOrWhiteSpace(power.PowerId);
        }

        private static HomecomingPowerReference Create(
            string categoryId,
            string powersetId,
            string powerId,
            string powersetDisplayName,
            string powerDisplayName,
            string iconIdentity) =>
            new(
                categoryId,
                powersetId,
                powerId,
                powersetDisplayName,
                powerDisplayName,
                iconIdentity,
                false,
                false,
                HomecomingPowerType.Click);
    }

    private sealed class RecordingInstalledGameAssetProvider : IInstalledGameAssetProvider
    {
        public List<string> Identities { get; } = [];

        public ImageSource? TryResolve(string? iconIdentity)
        {
            if (iconIdentity is null)
            {
                return null;
            }

            Identities.Add(iconIdentity);
            return new DrawingImage();
        }
    }

    private sealed class RecordingEnhancementIconCompositor : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];

        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            return new DrawingImage();
        }
    }
}
