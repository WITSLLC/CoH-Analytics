using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

[Collection(CoHAnalytics.Tests.WpfDispatcherCollection.Name)]
public sealed class LiveSessionViewModelRegressionTests
{
    [Fact]
    public void Clear_session_resets_displayed_totals_but_keeps_live_state()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 1_000,
                influence: 200,
                salvage: ("Luck Charm", 2))
        };

        using var viewModel = CreateViewModel(identity);
        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal(Format(1_000), viewModel.SessionExperienceLabel);
        Assert.Single(viewModel.SalvageDrops);
        Assert.Equal("Luck Charm", viewModel.SalvageDrops[0].DisplayName);
        Assert.Equal("2", viewModel.SalvageDrops[0].QuantityLabel);

        viewModel.ClearSessionCommand.Execute(null);

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("0", viewModel.SessionExperienceLabel);
        Assert.Equal("0", viewModel.SessionGameplayInfluenceLabel);
        Assert.Single(viewModel.SalvageDrops);
        Assert.Equal("2", viewModel.SalvageDrops[0].QuantityLabel);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 1_500,
            influence: 350,
            salvage: ("Luck Charm", 3));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal(Format(500), viewModel.SessionExperienceLabel);
        Assert.Equal(Format(150), viewModel.SessionGameplayInfluenceLabel);
        Assert.Single(viewModel.SalvageDrops);
        Assert.Equal("3", viewModel.SalvageDrops[0].QuantityLabel);
    }

    [Fact]
    public void Salvage_totals_project_into_loot_panel()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                salvage: ("Luck Charm", 3),
                enhancement: ("Accuracy SO", 1))
        };

        using var viewModel = CreateViewModel(identity);

        DrainDispatcher();

        Assert.True(viewModel.HasSalvageDrops);
        Assert.Equal("Luck Charm", viewModel.SalvageDrops[0].DisplayName);
        Assert.Equal("3", viewModel.SalvageDrops[0].QuantityLabel);
        Assert.True(viewModel.HasEnhancementDrops);
        Assert.Equal("Accuracy SO", viewModel.EnhancementDrops[0].DisplayName);
    }

    [Fact]
    public void Clear_session_keeps_salvage_totals_cumulative()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 0, 10, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                salvage: ("Fortune", 4))
        };

        using var viewModel = CreateViewModel(identity);
        Assert.Single(viewModel.SalvageDrops);
        Assert.Equal("Fortune", viewModel.SalvageDrops[0].DisplayName);
        Assert.Equal("4", viewModel.SalvageDrops[0].QuantityLabel);

        viewModel.ClearSessionCommand.Execute(null);

        Assert.Single(viewModel.SalvageDrops);
        Assert.Equal("4", viewModel.SalvageDrops[0].QuantityLabel);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            salvage: ("Fortune", 5));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Single(viewModel.SalvageDrops);
        Assert.Equal("Fortune", viewModel.SalvageDrops[0].DisplayName);
        Assert.Equal("5", viewModel.SalvageDrops[0].QuantityLabel);
    }

    [Fact]
    public void Clear_session_keeps_inspiration_totals_cumulative()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 0, 10, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                inspiration: ("Luck", 4),
                respite: ("Respite", 2))
        };

        using var viewModel = CreateViewModel(identity);
        Assert.Equal(2, viewModel.InspirationDrops.Count);
        Assert.Equal("4", viewModel.InspirationDrops.First(row => row.DisplayName == "Luck").QuantityLabel);

        viewModel.ClearSessionCommand.Execute(null);

        Assert.Equal(2, viewModel.InspirationDrops.Count);
        Assert.Equal("4", viewModel.InspirationDrops.First(row => row.DisplayName == "Luck").QuantityLabel);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            inspiration: ("Luck", 5));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Single(viewModel.InspirationDrops);
        Assert.Equal("Luck", viewModel.InspirationDrops[0].DisplayName);
        Assert.Equal("5", viewModel.InspirationDrops[0].QuantityLabel);
    }

    [Fact]
    public void Character_picker_remains_open_during_live_identity_refresh()
    {
        var contextId = MonitoringContextId.CreateNew();
        var characterId = CharacterRecordId.CreateNew();
        var identity = new FakeIdentityReadService
        {
            Current = ManualSelectionSnapshot(contextId, characterId, revision: 1)
        };

        using var viewModel = CreateViewModel(identity);
        var originalPanel = Assert.Single(viewModel.Contexts);
        viewModel.OpenCharacterPickerCommand.Execute(null);

        Assert.True(originalPanel.IsPickerOpen);
        Assert.Equal(characterId, originalPanel.SelectedPickerCharacter?.RecordId);
        Assert.Same(originalPanel, viewModel.ViewedContextPanel);

        identity.Current = ManualSelectionSnapshot(contextId, characterId, revision: 2);
        identity.RaiseChanged();
        DrainDispatcher();

        var refreshedPanel = Assert.Single(viewModel.Contexts);
        Assert.True(refreshedPanel.IsPickerOpen);
        Assert.Equal(characterId, refreshedPanel.SelectedPickerCharacter?.RecordId);
    }

    [Fact]
    public void Loot_headers_count_unique_rows_and_use_requested_expansion_defaults()
    {
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                MonitoringContextId.CreateNew(),
                DateTimeOffset.UtcNow,
                salvage: ("Fortune", 42),
                enhancement: ("Accuracy SO", 8),
                inspiration: ("Luck", 12))
        };

        using var viewModel = CreateViewModel(identity);

        Assert.Equal("Salvage (1)", viewModel.SalvageSectionHeader);
        Assert.Equal("Enhancements (1)", viewModel.EnhancementSectionHeader);
        Assert.Equal("Inspirations (1)", viewModel.InspirationSectionHeader);
        Assert.True(viewModel.IsSalvageExpanded);
        Assert.False(viewModel.IsEnhancementsExpanded);
        Assert.False(viewModel.IsInspirationsExpanded);

        viewModel.IsSalvageExpanded = false;
        viewModel.IsEnhancementsExpanded = true;

        Assert.Single(viewModel.SalvageDrops);
        Assert.Single(viewModel.EnhancementDrops);
        Assert.Equal("42", viewModel.SalvageDrops[0].QuantityLabel);
    }

    [Fact]
    public void Empty_loot_sections_report_zero_unique_rows()
    {
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(MonitoringContextId.CreateNew(), DateTimeOffset.UtcNow)
        };

        using var viewModel = CreateViewModel(identity);

        Assert.Equal("Salvage (0)", viewModel.SalvageSectionHeader);
        Assert.Equal("Enhancements (0)", viewModel.EnhancementSectionHeader);
        Assert.Equal("Inspirations (0)", viewModel.InspirationSectionHeader);
    }

    [Fact]
    public void Salvage_rows_project_authoritative_catalog_rarity_with_unknown_fallback()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);

        var identity = new FakeIdentityReadService
        {
            Current = SnapshotWithSalvage(
                MonitoringContextId.CreateNew(),
                DateTimeOffset.UtcNow,
                ("Human Blood Sample", 1),
                ("Mutant Blood Sample", 1),
                ("Alien Blood Sample", 1),
                ("Uncatalogued Salvage", 1))
        };

        using var viewModel = CreateViewModel(identity, catalog);

        Assert.Equal(
            "ECUncommon",
            viewModel.SalvageDrops.Single(row => row.DisplayName == "Mutant Blood Sample").RarityCode);
        Assert.Equal(
            "ECRare",
            viewModel.SalvageDrops.Single(row => row.DisplayName == "Alien Blood Sample").RarityCode);
        Assert.Null(
            viewModel.SalvageDrops.Single(row => row.DisplayName == "Human Blood Sample").RarityCode);
        Assert.Null(
            viewModel.SalvageDrops.Single(row => row.DisplayName == "Uncatalogued Salvage").RarityCode);
    }

    [Fact]
    public void Nightmare_obol_row_uses_requested_order_color_and_clear_session_baseline()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                currency: ("Nightmare Obol", 15))
        };

        using var viewModel = CreateViewModel(identity);

        string[] expectedOrder =
        [
            "Reward Merits",
            "Vanguard Merits",
            "Astral Merits",
            "Empyrean Merits",
            "Incarnate Threads",
            "Incarnate Shards",
            "Unstable Aether",
            "Prismatic Aether",
            "Nightmare Obol"
        ];
        Assert.Equal(expectedOrder, viewModel.Currencies.Select(row => row.DisplayName));

        var obol = viewModel.Currencies.Single(row => row.SourceKey == "Nightmare Obol");
        Assert.Equal("15", obol.QuantityLabel);
        Assert.Equal(LiveSessionCurrencyNameColor.Orange, obol.NameColor);

        viewModel.ClearSessionCommand.Execute(null);
        Assert.Equal("15", obol.QuantityLabel);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            currency: ("Nightmare Obol", 20));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("20", obol.QuantityLabel);
    }

    [Fact]
    public void Currency_rows_expose_requested_name_color_mapping()
    {
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(MonitoringContextId.CreateNew(), DateTimeOffset.UtcNow)
        };

        using var viewModel = CreateViewModel(identity);

        LiveSessionCurrencyNameColor[] expectedColors =
        [
            LiveSessionCurrencyNameColor.Bronze,
            LiveSessionCurrencyNameColor.Gold,
            LiveSessionCurrencyNameColor.Blue,
            LiveSessionCurrencyNameColor.Orange,
            LiveSessionCurrencyNameColor.White,
            LiveSessionCurrencyNameColor.White,
            LiveSessionCurrencyNameColor.Amber,
            LiveSessionCurrencyNameColor.Purple,
            LiveSessionCurrencyNameColor.Orange
        ];
        Assert.Equal(expectedColors, viewModel.Currencies.Select(row => row.NameColor));
    }

    private static LiveSessionViewModel CreateViewModel(
        FakeIdentityReadService identity,
        IItemReferenceCatalog? itemReferenceCatalog = null) =>
        TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            itemReferenceCatalog: itemReferenceCatalog);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        MonitoringContextId contextId,
        DateTimeOffset startedAt,
        long experience = 0,
        long influence = 0,
        (string Name, long Quantity)? salvage = null,
        (string Name, long Quantity)? enhancement = null,
        (string Name, long Quantity)? inspiration = null,
        (string Name, long Quantity)? respite = null,
        (string Name, long Quantity)? currency = null)
    {
        IReadOnlyList<GameplaySessionItemTotal> salvageTotals = salvage is null
            ? Array.Empty<GameplaySessionItemTotal>()
            : [new GameplaySessionItemTotal { DisplayName = salvage.Value.Name, Quantity = salvage.Value.Quantity }];

        IReadOnlyList<GameplaySessionItemTotal> enhancementTotals = enhancement is null
            ? Array.Empty<GameplaySessionItemTotal>()
            : [new GameplaySessionItemTotal { DisplayName = enhancement.Value.Name, Quantity = enhancement.Value.Quantity }];

        IReadOnlyList<GameplaySessionItemTotal> inspirationTotals = BuildInspirationTotals(inspiration, respite);
        IReadOnlyList<GameplaySessionRewardCurrencyTotal> currencyTotals = currency is null
            ? Array.Empty<GameplaySessionRewardCurrencyTotal>()
            :
            [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = currency.Value.Name,
                    Quantity = currency.Value.Quantity
                }
            ];

        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            CharacterDisplayName = "Example Hero",
            SessionStartedAt = startedAt,
            SessionExperienceGained = experience,
            SessionGameplayInfluenceGained = influence,
            RewardCurrencyTotals = currencyTotals,
            SalvageTotals = salvageTotals,
            EnhancementTotals = enhancementTotals,
            InspirationTotals = inspirationTotals
        };

        return GameplaySessionIdentityReadModelSnapshot.Create([context], DateTimeOffset.UtcNow, 1);
    }

    private static IReadOnlyList<GameplaySessionItemTotal> BuildInspirationTotals(
        (string Name, long Quantity)? luck,
        (string Name, long Quantity)? respite)
    {
        var totals = new List<GameplaySessionItemTotal>();
        if (luck is not null)
        {
            totals.Add(new GameplaySessionItemTotal
            {
                DisplayName = luck.Value.Name,
                Quantity = luck.Value.Quantity
            });
        }

        if (respite is not null)
        {
            totals.Add(new GameplaySessionItemTotal
            {
                DisplayName = respite.Value.Name,
                Quantity = respite.Value.Quantity
            });
        }

        return totals;
    }

    private static GameplaySessionIdentityReadModelSnapshot SnapshotWithSalvage(
        MonitoringContextId contextId,
        DateTimeOffset startedAt,
        params (string Name, long Quantity)[] salvage)
    {
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            CharacterDisplayName = "Example Hero",
            SessionStartedAt = startedAt,
            SalvageTotals = salvage
                .Select(item => new GameplaySessionItemTotal
                {
                    DisplayName = item.Name,
                    Quantity = item.Quantity
                })
                .ToArray()
        };

        return GameplaySessionIdentityReadModelSnapshot.Create([context], DateTimeOffset.UtcNow, 1);
    }

    private static GameplaySessionIdentityReadModelSnapshot ManualSelectionSnapshot(
        MonitoringContextId contextId,
        CharacterRecordId characterId,
        long revision)
    {
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.IdentityRequired,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Unknown,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            SessionStartedAt = DateTimeOffset.UtcNow,
            RequiresManualSelection = true,
            IdentityStatusLabel = "Identity Required",
            IdentityDetail = "Select the active character.",
            PickerCharacters =
            [
                new CharacterPickerOptionReadModel
                {
                    RecordId = characterId,
                    DisplayName = "Example Hero",
                    LastObservedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        return GameplaySessionIdentityReadModelSnapshot.Create(
            [context],
            DateTimeOffset.UtcNow,
            revision);
    }

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

        public void RaiseChanged() =>
            Changed?.Invoke(
                this,
                new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = Current });
    }

    private sealed class FakeApplicationOrchestrator : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; set; } =
            ApplicationStateSnapshot.Empty(DateTimeOffset.UtcNow);

        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeGameplaySessionManager : IGameplaySessionManager
    {
        public GameplaySessionManagerSnapshot Current { get; set; } = GameplaySessionManagerSnapshot.Empty;

        public GameplaySessionDiagnostics Diagnostics { get; set; } =
            GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(isRunning: false);

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

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

        public GameplaySessionDiagnostics GetDiagnostics() => Diagnostics;
    }
}
