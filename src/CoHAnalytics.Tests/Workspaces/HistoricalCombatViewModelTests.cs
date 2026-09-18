using System.Xml.Linq;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class HistoricalCombatViewModelTests
{
    [Fact]
    public void Defaults_choose_latest_globally_and_parents_filter_by_stable_identity()
    {
        using var f = new Fixture();
        var vm = f.Create();
        Assert.Null(vm.SelectedSegment);
        Assert.False(vm.ReportCommand.CanExecute(null));
        vm.Refresh();
        Assert.Equal("RivenForest", vm.SelectedAccount!.Id);
        Assert.Equal(f.Riven, vm.SelectedCharacter!.Id);
        Assert.Equal(f.Latest.GameplaySessionId, vm.SelectedSegment!.Header.GameplaySessionId);
        Assert.Single(vm.CharacterChoices);
        vm.SelectedAccount = vm.AccountsChoices.Single(a => a.Id == "Adelbert");
        Assert.Equal(2, vm.CharacterChoices.Count);
        Assert.All(vm.CharacterChoices, c => Assert.Equal("Adelbert", c.AccountId));
        Assert.All(vm.SegmentChoices, s => Assert.Equal(f.Adelbert, s.Header.CanonicalCharacterRecordId));
        Assert.Equal(f.Adelbert, vm.SelectedCharacter!.Id); // Same name across accounts is not identity.
        vm.SelectedSegment = vm.SegmentChoices.Last();
        var selected = vm.SelectedSegment.Header.SegmentId;
        vm.Refresh();
        Assert.Equal("Adelbert", vm.SelectedAccount!.Id);
        Assert.Equal(selected, vm.SelectedSegment!.Header.SegmentId);
        vm.SelectedCharacter = vm.CharacterChoices.Single(c => c.Id == f.EmptyCharacter);
        Assert.Empty(vm.SegmentChoices);
        Assert.Null(vm.SelectedSegment);
        Assert.Null(vm.ProjectionView);
        Assert.False(vm.ReportCommand.CanExecute(null));
        vm.Refresh();
        Assert.Equal(f.EmptyCharacter, vm.SelectedCharacter!.Id); // No forced return to last played.
    }

    [Fact]
    public void Clearing_parents_clears_downstream_context_and_foreign_selection_is_rejected()
    {
        using var f = new Fixture();
        var vm = f.Create(); vm.Refresh();
        var foreign = vm.SelectedSegment;
        vm.SelectedAccount = null;
        Assert.Null(vm.SelectedCharacter);
        Assert.Null(vm.SelectedSegment);
        Assert.Empty(vm.CharacterChoices);
        Assert.Empty(vm.SegmentChoices);
        Assert.Null(vm.ProjectionView);
        Assert.Null(vm.CaptureStart);
        Assert.False(vm.CanEditRunName);
        vm.SelectedSegment = foreign;
        Assert.Null(vm.SelectedSegment);
        Assert.False(vm.ReportCommand.CanExecute(null));
    }

    [Fact]
    public void Name_roundtrips_through_existing_sidecar_without_changing_immutable_files()
    {
        using var f = new Fixture();
        var vm = f.Create(); vm.Refresh();
        var header = vm.SelectedSegment!.Header;
        var files = Directory.GetFiles(f.DirectoryFor(f.Latest))
            .Where(p => Path.GetFileName(p) != SegmentStore.AnnotationsFileName)
            .ToDictionary(p => p, File.ReadAllBytes);
        // A separate annotation edit after selection must survive the rename.
        Assert.True(f.Store.TryUpdateAnnotations(header.GameplaySessionId, header.SegmentOrdinal,
            new SegmentAnnotations { Note = "Keep this note", IsBeta = true, IncludeInOverview = false }).IsSuccess);
        vm.RunName = "  Warrior Earth Farm  ";
        Assert.True(vm.SaveRunNameCommand.CanExecute(null));
        vm.SaveRunNameCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.SaveRunNameCommand.CanExecute(null));
        Assert.Equal(header, vm.SelectedSegment!.Header);
        Assert.StartsWith("Warrior Earth Farm — ", vm.SelectedSegment.Label);
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        var reloaded = f.Create(); reloaded.Refresh();
        Assert.Equal("Warrior Earth Farm", reloaded.RunName);
        var stored = f.Reader.TryLoad(header.SegmentId).Segment!.Annotations!;
        Assert.Equal("Keep this note", stored.Note);
        Assert.True(stored.IsBeta);
        Assert.False(stored.IncludeInOverview);
        reloaded.RunName = "";
        reloaded.SaveRunNameCommand.Execute(null);
        Assert.Null(f.Reader.TryLoad(header.SegmentId).Segment!.Annotations!.UserDisplayName);
        Assert.DoesNotContain(" — ", reloaded.SelectedSegment!.Label);
    }

    [Fact]
    public void Refresh_retains_an_unsaved_name_for_the_same_segment_but_selection_change_discards_it()
    {
        using var f = new Fixture();
        var vm = f.Create(); vm.Refresh();
        vm.RunName = "Draft";
        vm.Refresh();
        Assert.Equal("Draft", vm.RunName);
        vm.SelectedAccount = vm.AccountsChoices.Single(a => a.Id == "Adelbert");
        Assert.NotEqual("Draft", vm.RunName);
    }

    [Fact]
    public void Failed_save_preserves_draft_and_disk_data_and_reports_a_safe_error()
    {
        using var f = new Fixture();
        var vm = f.Create(new FailingWriter()); vm.Refresh();
        vm.RunName = "Unsaved";
        vm.SaveRunNameCommand.Execute(null);
        Assert.Equal("Unsaved", vm.RunName);
        Assert.NotNull(vm.ErrorMessage);
        Assert.DoesNotContain("private-path", vm.ErrorMessage);
        Assert.Null(f.Reader.TryLoad(vm.SelectedSegment!.Header.SegmentId).Segment!.Annotations!.UserDisplayName);
    }

    [Fact]
    public void Corrupt_annotation_is_not_overwritten_and_does_not_prevent_reporting()
    {
        using var f = new Fixture();
        var path = Path.Combine(f.DirectoryFor(f.Latest), SegmentStore.AnnotationsFileName);
        File.WriteAllText(path, "broken");
        var vm = f.Create(); vm.Refresh();
        Assert.False(vm.CanEditRunName);
        Assert.False(vm.SaveRunNameCommand.CanExecute(null));
        Assert.True(vm.ReportCommand.CanExecute(null));
        vm.RunName = "Do not overwrite";
        vm.SaveRunNameCommand.Execute(null);
        Assert.Equal("broken", File.ReadAllText(path));
    }

    [Fact]
    public void Legacy_is_selectable_and_reportable_but_never_writes_a_run_name()
    {
        using var f = new Fixture();
        var vm = f.Create(); vm.Refresh();
        vm.SelectedAccount = vm.AccountsChoices.Single(a => a.Id == "Adelbert");
        vm.SelectedSegment = vm.SegmentChoices.Single(s => s.Header.CaptureKind == HistoricalCaptureKind.LegacyObservation);
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalLegacy, vm.ProjectionView!.SourceKind);
        Assert.False(vm.CanEditRunName);
        Assert.False(vm.SaveRunNameCommand.CanExecute(null));
        Assert.True(vm.ReportCommand.CanExecute(null));
        Assert.Null(vm.CharacterDetails); // Current character metadata is not a captured build.
        Assert.Null(vm.BuildLabel);
        var filesBefore = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        vm.RunName = "Unsupported";
        vm.SaveRunNameCommand.Execute(null);
        Assert.Equal(filesBefore, Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Order().ToArray());
        vm.ReportCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(SegmentReportService.GetFileName(vm.SelectedSegment.Header), Path.GetFileName(f.Browser.Paths.Single()));
    }

    [Fact]
    public void Report_uses_selected_capture_and_existing_deterministic_service_path()
    {
        using var f = new Fixture();
        var vm = f.Create(); vm.Refresh();
        vm.SelectedAccount = vm.AccountsChoices.Single(a => a.Id == "Adelbert");
        vm.SelectedSegment = vm.SegmentChoices.Single(s => s.Header.CaptureKind == HistoricalCaptureKind.DurableSegment);
        vm.ReportCommand.Execute(null);
        vm.ReportCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, f.Browser.Paths.Count);
        Assert.Equal(f.Browser.Paths[0], f.Browser.Paths[1]);
        Assert.Equal(SegmentReportService.GetFileName(vm.SelectedSegment.Header), Path.GetFileName(f.Browser.Paths[0]));
        Assert.StartsWith("<!DOCTYPE html>", File.ReadAllText(f.Browser.Paths[0]));
        vm.SelectedSegment = null;
        Assert.False(vm.ReportCommand.CanExecute(null));
        vm.ReportCommand.Execute(null);
        Assert.Equal(2, f.Browser.Paths.Count);
    }

    [Fact]
    public void Removed_or_unreadable_capture_clears_context_and_disables_report()
    {
        using var f = new Fixture();
        var vm = f.Create(); vm.Refresh();
        File.WriteAllText(Path.Combine(f.DirectoryFor(f.Latest), SegmentStore.AggregatesFileName), "broken");
        vm.Refresh();
        Assert.Null(vm.ProjectionView);
        Assert.False(vm.ReportCommand.CanExecute(null));
        Assert.False(vm.CanEditRunName);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public void Context_uses_capture_metadata_and_exact_projection_without_technical_diagnostics()
    {
        using var f = new Fixture();
        var vm = f.Create(); vm.Refresh();
        Assert.Equal("Hell's Vengence", vm.CharacterName);
        Assert.Equal("Level 32  |  Brute  |  Fire Melee / Fire Aura", vm.CharacterDetails);
        Assert.Equal("5:00", vm.SessionLength);
        Assert.Null(vm.BuildLabel);
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalDurable, vm.ProjectionView!.SourceKind);
        Assert.Equal(f.Latest.Aggregates.Clock, vm.ProjectionView.Projection.Clock);
        Assert.Equal(CombatSectionId.Offense, vm.SelectedSection);
        Assert.Equal(new[] { "Offense", "Defense", "Healing", "Pets" }, vm.Sections.Select(s => s.Label));
        foreach (var section in vm.Sections)
        {
            vm.SelectSectionCommand.Execute(section.Id);
            Assert.Equal(section.Id, vm.SelectedSection);
            Assert.Same(section, Assert.Single(vm.Sections, s => s.IsActive));
        }
    }

    [Fact]
    public void Empty_history_has_no_default_and_no_enabled_actions()
    {
        var vm = new HistoricalCombatViewModel(null, null, null, new AccountAnonymityService(), null, null);
        vm.Refresh();
        Assert.Empty(vm.AccountsChoices);
        Assert.Null(vm.SelectedSegment);
        Assert.False(vm.ReportCommand.CanExecute(null));
        Assert.False(vm.SaveRunNameCommand.CanExecute(null));
    }

    [Fact]
    public void Combat_xaml_contains_historical_selectors_and_no_live_scope_or_status_controls()
    {
        var root = FindRoot();
        var combat = XDocument.Load(Path.Combine(root, "src/CoHAnalytics/Workspaces/HistoricalCombatView.xaml"));
        var text = combat.ToString();
        foreach (var binding in new[] { "SelectedAccount", "SelectedCharacter", "SelectedSegment", "ReportCommand", "SaveRunNameCommand", "Sections" })
            Assert.Contains(binding, text);
        foreach (var forbidden in new[] { "Rolling", "Tracked", "CombatStatus", "COMBAT STATUS", "ManifestHash", "Fingerprint", "Zone" })
            Assert.DoesNotContain(forbidden, text);
        var workspace = File.ReadAllText(Path.Combine(root, "src/CoHAnalytics/Workspaces/AnalyticsView.xaml"));
        Assert.Contains("ShowCompareContent", workspace);
        Assert.Contains("HistoricalCombatView", workspace);
        Assert.DoesNotContain("CombatStatus", workspace);
        Assert.DoesNotContain("RollingPresets", workspace);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src/CoHAnalytics.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository not found.");
    }

    private sealed class FailingWriter : ISegmentAnnotationWriter
    {
        public SegmentPersistResult TryUpdateAnnotations(GameplaySessionId id, int ordinal, SegmentAnnotations annotations) =>
            new() { Outcome = SegmentPersistOutcome.PersistenceFailed, Detail = "private-path" };
    }

    internal sealed class RecordingBrowser : IReportBrowserLauncher
    {
        public List<string> Paths { get; } = [];
        public void Open(string path) => Paths.Add(path);
    }

    internal sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "coh-historical-combat", Guid.NewGuid().ToString("N"));
        public CharacterRepository Characters { get; }
        public SegmentStore Store { get; }
        public HistoricalSegmentReadService Reader { get; }
        public RecordingBrowser Browser { get; } = new();
        public CharacterRecordId Adelbert { get; }
        public CharacterRecordId Riven { get; }
        public CharacterRecordId EmptyCharacter { get; }
        public SegmentDraft Latest { get; }

        public Fixture()
        {
            Characters = new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = Root });
            Adelbert = Characters.EstablishTrustedFromWelcome("Adelbert", "Hell's Vengence").RecordId!;
            Riven = Characters.EstablishTrustedFromWelcome("RivenForest", "Hell's Vengence").RecordId!;
            EmptyCharacter = Characters.EstablishTrustedFromWelcome("Adelbert", "No history").RecordId!;
            Store = new SegmentStore(Root);
            var observations = new CharacterPerformanceObservationRepository(Root);
            var start = new DateTimeOffset(2026, 9, 10, 20, 14, 0, TimeSpan.Zero);
            observations.Persist(new CharacterPerformanceObservation
            {
                GameplaySessionId = GameplaySessionId.CreateNew(), SegmentOrdinal = 0, CharacterRecordId = Adelbert,
                StartedAtUtc = start.AddDays(-1), EndedAtUtc = start.AddDays(-1).AddMinutes(5)
            });
            Publish(Adelbert, "Adelbert", start);
            Latest = Publish(Riven, "RivenForest", start.AddDays(1));
            Reader = new HistoricalSegmentReadService(Store, observations, Characters);
        }

        private SegmentDraft Publish(CharacterRecordId character, string account, DateTimeOffset start)
        {
            var engine = new CombatEngine(); engine.Freeze();
            var projection = engine.Project(new SegmentClockCapture
            { CaptureStartUtc = start, CaptureEndUtc = start.AddMinutes(5), AsOfUtc = start.AddMinutes(5) });
            var draft = new SegmentDraft
            {
                GameplaySessionId = GameplaySessionId.CreateNew(), CharacterRecordId = character, AccountStableId = account,
                CharacterDisplayNameAtCapture = "Hell's Vengence", LevelAtCapture = 32, Archetype = "Brute",
                PrimaryPowerSet = "Fire Melee", SecondaryPowerSet = "Fire Aura",
                CaptureStartUtc = start, CaptureEndUtc = start.AddMinutes(5), FinalizedAtUtc = start.AddMinutes(5),
                Aggregates = projection, Spine = [], Coverage = new SegmentCoverageDescriptor
                {
                    LogicalEventCount = 0, DuplicateOccurrencesIgnored = 0, RetainedSpineEventCount = 0,
                    SpineRetentionLimit = SegmentSpineLimits.MaxRetainedLogicalEvents, SpineTruncated = false,
                    Replay = LosslessReplayCoverageMatrix.ForCapture(false, true, false)
                }
            };
            Assert.True(Store.Persist(draft).IsSuccess);
            return draft;
        }

        public HistoricalCombatViewModel Create(ISegmentAnnotationWriter? writer = null) => new(
            Reader, Characters, null, new AccountAnonymityService(), writer ?? Store,
            new SegmentReportService(Reader, Browser, Root));

        public string DirectoryFor(SegmentDraft draft) => Store.TryLoad(draft.GameplaySessionId, draft.SegmentOrdinal).Segment!.PublishedDirectory!;

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
