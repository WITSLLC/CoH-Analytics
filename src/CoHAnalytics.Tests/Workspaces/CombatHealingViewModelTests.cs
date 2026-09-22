using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Homecoming;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class CombatHealingViewModelTests
{
    [Fact]
    public void Healing_shows_only_player_output_and_suppresses_pet_inclusive_session_totals()
    {
        var vm = new CombatHealingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    HealingDealt = Metric<CombatScaledAmount>.Available(new(2215)),
                    HealingReceived = Metric<CombatScaledAmount>.Available(new(800)),
                    EnduranceGranted = Metric<CombatScaledAmount>.Available(new(1500)),
                    EnduranceReceived = Metric<CombatScaledAmount>.Available(new(400)),
                    DamageDealt = Metric<CombatScaledAmount>.Available(new(99999))
                }
            },
            Powers =
            [
                Heal("Transfusion"),
                Heal("Transfusion") with { Scope = CombatAnalyticsScope.OwnPetsAggregate, HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(400)) },
                Heal("Reconstruction") with { Direction = CombatAnalyticsDirection.Incoming, HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(800)) },
                Endurance("Stamina"),
                Endurance("Recovery Aura") with { Direction = CombatAnalyticsDirection.Incoming, EnduranceMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(400)) },
                Heal("Whispered") with { HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(CombatScaledAmount.Zero) },
                Heal("Missing") with { HealingMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured() },
                Heal("Scratch") with { HealingMagnitudeMetric = Metric<CombatScaledAmount>.Unsupported() },
                Heal("Per pet") with { Scope = CombatAnalyticsScope.PerPet },
                CombatOffenseViewModelTests.Power("Burn")
            ]
        });
        Assert.Empty(vm.Summary);
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("HPS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("overheal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("effective", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("efficiency", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, vm.Powers.Count);
        Assert.Equal("Transfusion", vm.Powers[0].Name);
        Assert.Equal("Healing Dealt", vm.Powers[0].Direction);
        Assert.Equal("Player", vm.Powers[0].Scope);
        Assert.Equal("50.01", vm.Powers[0].Amount);
        Assert.Equal("8", vm.Powers[0].Events);
        Assert.All(vm.Powers, p =>
        {
            Assert.Equal(CombatAnalyticsDirection.Outgoing, p.Source.Direction);
            Assert.Equal(CombatAnalyticsScope.Self, p.Source.Scope);
        });
        Assert.Equal("Endurance Granted", vm.Powers.Single(p => p.Name == "Stamina").Direction);
        Assert.DoesNotContain(vm.Powers, p => p.Name is "Burn" or "Whispered" or "Missing" or "Scratch" or "Per pet");
        Assert.DoesNotContain(vm.Powers.SelectMany(p => p.Details), d => d.Label.Contains("Distinct target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Powers.SelectMany(p => p.Details), d => d.Label.Contains("Recipient", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Powers.SelectMany(p => p.Details), d => d.Label.Contains("Maximum hit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains('%') || v.Value.Contains('%'));
        Assert.Equal("Transfusion", Assert.Single(vm.HealingByPower).Label);
        Assert.Equal("Stamina", Assert.Single(vm.EnduranceByPower).Label);
        Assert.Same(vm.Powers[0], vm.SelectedPower);
    }

    [Fact]
    public void Player_self_heal_survives_engine_projection_without_build_membership()
    {
        var engine = new CombatEngine();
        var observedAt = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        engine.Apply(new CanonicalCombatEvent
        {
            Provenance = new EventProvenance
            {
                ContextId = MonitoringContextId.CreateNew(), SourceId = "healing-test", AccountStableId = "account",
                SourceSegmentId = Guid.NewGuid(), BindingGeneration = 1, ParserSequence = 1,
                ByteStart = 0, ByteEnd = 80, ObservedAt = observedAt,
                GrammarSetVersion = EventProvenance.CurrentGrammarSetVersion, ClassificationRuleId = "self-heal"
            },
            Sequence = 1, ObservedAt = observedAt,
            Family = CombatEventFamily.HealDealt, GrammarId = CombatGrammarId.Heal02YouHealYourself,
            Actor = ActorRef.Self, Target = ActorRef.SelfNamed("yourself"), PowerName = "Reconstruction",
            Facets = EventFacets.HealDelivered, Magnitude = MagnitudeKind.HitPoints, Amount = new(1250),
            MirrorClass = new() { Family = CombatEventFamily.HealDealt, GrammarId = CombatGrammarId.Heal02YouHealYourself }
        });
        var vm = new CombatHealingViewModel();
        // The frozen build contains Burn only; build membership is not an inclusion requirement.
        vm.SetProjection(engine.Project(), CombatOffenseViewModelTests.Manifest());
        var row = Assert.Single(vm.Powers);
        Assert.Equal("Reconstruction", row.Name);
        Assert.Equal("12.50", row.Amount);
        Assert.Equal(CombatAnalyticsDirection.Outgoing, row.Source.Direction);
        Assert.Equal(CombatAnalyticsScope.Self, row.Source.Scope);
        Assert.Same(row, vm.SelectedPower);
        Assert.Equal("Reconstruction", Assert.Single(vm.HealingByPower).Label);
        Assert.Empty(vm.Summary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Player_support_preserves_partial_metrics_and_suppresses_missing_or_zero(bool endurance)
    {
        CombatPowerAnalysisRow Row(string name, Metric<CombatScaledAmount> metric) => endurance
            ? Endurance(name) with { EnduranceMagnitudeMetric = metric }
            : Heal(name) with { HealingMagnitudeMetric = metric };
        var vm = new CombatHealingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Row("Partial support", Metric<CombatScaledAmount>.Incomplete(new(250))),
                Row("Missing", Metric<CombatScaledAmount>.NotCaptured()),
                Row("Unsupported", Metric<CombatScaledAmount>.Unsupported()),
                Row("Zero", Metric<CombatScaledAmount>.Available(CombatScaledAmount.Zero))]
        }, CombatOffenseViewModelTests.Manifest());
        var row = Assert.Single(vm.Powers);
        Assert.Equal("Partial support", row.Name);
        Assert.Equal("2.50", row.Amount);
        Assert.Contains("Partial", row.Note);
        Assert.Equal("Partial support", Assert.Single(endurance ? vm.EnduranceByPower : vm.HealingByPower).Label);
        Assert.Empty(endurance ? vm.HealingByPower : vm.EnduranceByPower);
    }

    [Fact]
    public void Mixed_healing_and_endurance_on_one_cube_stay_separate_rows()
    {
        var vm = new CombatHealingViewModel();
        var mixed = Heal("Transfusion") with { EnduranceMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(250)) };
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [mixed] });
        Assert.Equal(2, vm.Powers.Count);
        Assert.All(vm.Powers, p => Assert.Same(mixed, p.Source));
        Assert.Equal("Healing Dealt", vm.Powers.Single(p => !p.IsEndurance).Direction);
        Assert.Equal("50.01", vm.Powers.Single(p => !p.IsEndurance).Amount);
        Assert.Equal("Endurance Granted", vm.Powers.Single(p => p.IsEndurance).Direction);
        Assert.Equal("2.50", vm.Powers.Single(p => p.IsEndurance).Amount);
        Assert.Single(vm.HealingByPower);
        Assert.Single(vm.EnduranceByPower);
    }

    [Fact]
    public void Missing_healing_metrics_stay_suppressed_and_clearing_projection_clears_selection()
    {
        var vm = new CombatHealingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                HealingDealt = new(8888),
                Metrics = CombatSessionMetricSet.Empty with { HealingDealt = Metric<CombatScaledAmount>.NotCaptured() }
            },
            Powers = [Heal("Transfusion") with { HealingMagnitudeMetric = Metric<CombatScaledAmount>.Unsupported() }]
        });
        Assert.Empty(vm.Summary);
        Assert.Empty(vm.Powers);
        Assert.True(vm.ShowEmptyMessage);
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Heal("Transfusion")] });
        Assert.NotNull(vm.SelectedPower);
        vm.SetProjection(null);
        Assert.Null(vm.SelectedPower);
        Assert.False(vm.HasSelectedPower);
        Assert.Empty(vm.Powers);
        Assert.Empty(vm.Summary);
    }

    [Fact]
    public void Incoming_support_matching_frozen_build_is_excluded_before_icon_matching()
    {
        var catalog = new Catalog();
        var assets = new Assets();
        var vm = new CombatHealingViewModel(catalog, assets);
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Heal("Burn") with { Direction = CombatAnalyticsDirection.Incoming },
                Endurance("Burn") with { Direction = CombatAnalyticsDirection.Incoming }]
        }, CombatOffenseViewModelTests.Manifest());
        Assert.Null(catalog.Last);
        Assert.Empty(vm.Powers);
        Assert.Empty(vm.HealingByPower);
        Assert.Empty(vm.EnduranceByPower);
        Assert.Null(vm.SelectedPower);
        Assert.True(vm.ShowEmptyMessage);
    }

    [Fact]
    public void Unique_outgoing_healing_power_icon_wins_over_generic_fallback()
    {
        using var fixture = HomecomingPowerReferenceFixture.Create();
        var catalog = fixture.CreateCatalog();
        var assets = new Assets();
        var compositor = new RecordingCompositor();
        var vm = new CombatHealingViewModel(catalog, assets, compositor: compositor);
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Heal("Brawl"), Heal("Unknown Healing")]
        });
        Assert.Equal("Inherent_Brawl.tga", assets.Last);
        Assert.Same(assets.Image, vm.Powers.Single(p => p.Name == "Brawl").Icon);
        Assert.NotSame(CombatOffenseViewModel.GenericDamageIcon, vm.Powers.Single(p => p.Name == "Brawl").Icon);
        AssertGenericFallback(vm.Powers.Single(p => p.Name == "Unknown Healing").Icon);
        Assert.Empty(compositor.Requests);
    }

    [Fact]
    public void Exact_outgoing_proc_identity_uses_composed_icon()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var compositor = new RecordingCompositor();
        var assets = new Assets();
        var vm = new CombatHealingViewModel(
            items: catalog, assets: assets, compositor: compositor, boostMetadata: new FakeBoostMetadata());
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Heal("Scirocco's Dervish: Chance for Lethal Damage")]
        });
        Assert.Null(assets.Last);
        Assert.Same(compositor.Image, vm.Powers.Single().Icon);
        Assert.NotSame(CombatOffenseViewModel.GenericDamageIcon, vm.Powers.Single().Icon);
        Assert.Single(compositor.Requests);
    }

    [Theory]
    [InlineData(CombatAnalyticsScope.OwnPetsAggregate)]
    [InlineData(CombatAnalyticsScope.PerPet)]
    public void Pet_support_is_excluded_from_table_details_and_lower_panels(CombatAnalyticsScope scope)
    {
        var compositor = new RecordingCompositor();
        var vm = new CombatHealingViewModel(compositor: compositor);
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Heal("Transfusion") with { Scope = scope }, Endurance("Recovery Aura") with { Scope = scope }]
        });
        Assert.Empty(vm.Powers);
        Assert.Empty(vm.HealingByPower);
        Assert.Empty(vm.EnduranceByPower);
        Assert.Null(vm.SelectedPower);
        Assert.Empty(compositor.Requests);
    }

    [Fact]
    public void Headers_sort_healing_events_without_changing_row_identity()
    {
        var vm = new CombatHealingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers =
            [
                Heal("Zulu") with { HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(1000)), EventCount = 2 },
                Heal("Alpha") with { HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(3000)), EventCount = 9 }
            ]
        });
        var selected = vm.SelectedPower;
        Assert.Equal("Alpha", selected!.Name);
        vm.SortPowersCommand.Execute("Events");
        Assert.Equal("Events ▾", vm.EventsColumnHeader);
        Assert.Equal(["Alpha", "Zulu"], vm.Powers.Select(p => p.Name));
        Assert.Same(selected, vm.SelectedPower);
        vm.SortPowersCommand.Execute("Power");
        Assert.Equal(["Zulu", "Alpha"], vm.Powers.Select(p => p.Name));
        vm.SortPowersCommand.Execute("Direction");
        Assert.Equal("Direction ▾", vm.DirectionColumnHeader);
    }

    [Fact]
    public void Segment_selection_refreshes_healing_without_changing_other_chip_state()
    {
        using var f = new HistoricalCombatViewModelTests.Fixture();
        var historical = f.Create(); historical.Refresh();
        historical.SelectSectionCommand.Execute(CombatSectionId.Healing);
        Assert.True(historical.IsHealingSelected);
        Assert.False(historical.IsOffenseSelected);
        Assert.False(historical.IsIncomingSelected);
        Assert.DoesNotContain(historical.Healing.Summary, v => v.Label == "Healing Dealt");
        historical.SelectedAccount = historical.AccountsChoices.Single(a => a.Id == "Adelbert");
        historical.SelectedSegment = historical.SegmentChoices.Single(s => s.Header.CaptureKind == HistoricalCaptureKind.LegacyObservation);
        Assert.Equal(CombatSectionId.Healing, historical.SelectedSection);
        Assert.Empty(historical.Healing.Powers);
        historical.SelectedSegment = null;
        Assert.Empty(historical.Healing.Summary);
        Assert.Null(historical.Healing.SelectedPower);
        Assert.Equal(new[] { "Offense", "Incoming", "Healing" }, historical.Sections.Select(s => s.Label));
        Assert.DoesNotContain(historical.Sections, s => s.Label == "Pets");
        Assert.DoesNotContain(historical.Offense.Summary, v => v.Label == "Total outgoing damage");
        Assert.DoesNotContain(historical.Incoming.Summary, v => v.Label == "Damage taken");
    }

    private static CombatPowerAnalysisRow Heal(string name) =>
        CombatOffenseViewModelTests.Power(name) with
        {
            HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(5001)),
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured()
        };

    private static CombatPowerAnalysisRow Endurance(string name) =>
        CombatOffenseViewModelTests.Power(name) with
        {
            EnduranceMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(1500)),
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured(),
            HealingMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured()
        };

    private static void AssertGenericFallback(ImageSource? icon)
    {
        Assert.Same(CombatOffenseViewModel.GenericDamageIcon, icon);
        var bitmap = Assert.IsType<BitmapImage>(icon);
        Assert.Equal(AssetUri.ForResource(CombatOffenseViewModel.GenericDamageIconResource), bitmap.UriSource);
    }

    private sealed class Catalog : IHomecomingPowerReferenceCatalog
    {
        public bool IsLoaded => true;
        public (string, string, string)? Last { get; private set; }
        public bool TryResolve(string categoryId, string powersetId, string powerId, out HomecomingPowerReference power)
        {
            Last = (categoryId, powersetId, powerId);
            power = new(categoryId, powersetId, powerId, "Fire Melee", "Burn", "existing-icon", false, false, HomecomingPowerType.Click);
            return true;
        }
        public bool TryResolveUniqueDisplayName(string? displayName, out HomecomingPowerReference power)
        {
            power = default;
            return false;
        }
    }

    private sealed class Assets : IInstalledGameAssetProvider
    {
        public string? Last { get; private set; }
        public ImageSource Image { get; } = new DrawingImage();
        public ImageSource? TryResolve(string? iconIdentity) { Last = iconIdentity; return Image; }
    }

    private sealed class RecordingCompositor : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];
        public ImageSource Image { get; } = new DrawingImage();
        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            return Image;
        }
    }

    private sealed class FakeBoostMetadata : IHomecomingBoostMetadataProvider
    {
        public IReadOnlyList<string>? TryGetBoostsAllowed(string? homecomingSourceId) =>
            string.IsNullOrWhiteSpace(homecomingSourceId) ? null : ["Damage"];
    }
}
