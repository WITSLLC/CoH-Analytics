using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class AnalyticsHistoricalSegmentBrowserTests
{
    [Fact]
    public void Report_preserves_selection_expansion_and_exclusion_and_surfaces_failures()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100, include: true);
        var reports = new RecordingReportService();
        using var viewModel = fixture.CreateViewModel(reportService: reports);
        var row = Assert.Single(viewModel.HistoricalSegments);
        viewModel.ReportHistoricalSegmentCommand.Execute(row);
        Assert.Equal(0, reports.Calls);
        viewModel.SelectHistoricalSegmentCommand.Execute(row);
        reports.Failure = "The report could not be saved.";
        viewModel.ReportHistoricalSegmentCommand.Execute(row);
        Assert.Equal(1, reports.Calls);
        Assert.Equal((row.GameplaySessionId, row.SegmentOrdinal), reports.LastCapture);
        Assert.Equal(reports.Failure, viewModel.HistoricalSegmentErrorMessage);
        Assert.True(viewModel.HasHistoricalSegmentError);
        Assert.Same(row, viewModel.SelectedHistoricalSegment);
        Assert.True(row.IsExpanded);
        Assert.True(row.IncludeInOverview);
        reports.Failure = null;
        viewModel.ReportHistoricalSegmentCommand.Execute(row);
        Assert.False(viewModel.HasHistoricalSegmentError);
        Assert.Same(row, viewModel.SelectedHistoricalSegment);
        Assert.True(row.IsExpanded);
        reports.Throws = true;
        viewModel.ReportHistoricalSegmentCommand.Execute(row);
        Assert.True(viewModel.HasHistoricalSegmentError);
        Assert.DoesNotContain("secret-path", viewModel.HistoricalSegmentErrorMessage!);
        viewModel.SelectHistoricalSegmentCommand.Execute(row);
        Assert.False(row.IsExpanded);
    }

    [Fact]
    public void Report_button_is_only_inside_expanded_detail_panel()
    {
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(LocateRepositoryRoot(), "src", "CoHAnalytics", "Workspaces", "AnalyticsView.xaml"));
        var button = Assert.Single(document.Descendants(), e => e.Name.LocalName == "Button" && (string?)e.Attribute("Content") == "Report");
        Assert.Contains("ReportHistoricalSegmentCommand", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding}", (string?)button.Attribute("CommandParameter"));
        Assert.Contains(button.Ancestors(), e => e.Name.LocalName == "Border"
            && ((string?)e.Attribute("Visibility"))?.Contains("IsExpanded", StringComparison.Ordinal) == true);
    }

    private sealed class RecordingReportService : ISegmentReportService
    {
        public int Calls { get; private set; }
        public (GameplaySessionId, int)? LastCapture { get; private set; }
        public string? Failure { get; set; }
        public bool Throws { get; set; }
        public string? GenerateAndOpen(GameplaySessionId sessionId, int segmentOrdinal)
        {
            Calls++;
            LastCapture = (sessionId, segmentOrdinal);
            if (Throws) throw new IOException("secret-path");
            return Failure;
        }
    }

    [Fact]
    public void Retained_rows_map_exclusion_expand_metrics_and_toggle_overview_immediately()
    {
        using var fixture = new Fixture();
        var included = fixture.Persist(fixture.Character, experience: 6_200_000, include: true);
        var excluded = fixture.Persist(
            fixture.Character,
            experience: 1_000,
            include: false,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        using var viewModel = fixture.CreateViewModel();

        Assert.Equal(2, viewModel.HistoricalSegments.Count);
        Assert.Equal(excluded.GameplaySessionId, viewModel.HistoricalSegments[0].GameplaySessionId);
        Assert.False(viewModel.HistoricalSegments[0].IncludeInOverview);
        Assert.True(viewModel.HistoricalSegments[0].IsExcluded);
        Assert.True(viewModel.HistoricalSegments[1].IncludeInOverview);
        Assert.False(viewModel.HistoricalSegments[1].IsExcluded);
        Assert.Equal("1 of 2", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("00:30:00", viewModel.Overview.ObservedDurationLabel);

        var includedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == included.GameplaySessionId);
        viewModel.SelectHistoricalSegmentCommand.Execute(includedRow);
        Assert.True(includedRow.IsExpanded);
        Assert.Equal("12M", includedRow.ExperiencePerHourLabel);
        Assert.Equal("0", includedRow.InfluencePerHourLabel);
        Assert.Equal("0", includedRow.DamagePerSecondLabel);
        Assert.Equal("—", includedRow.AccuracyLabel);

        viewModel.ToggleHistoricalSegmentExclusionCommand.Execute(includedRow);
        DrainDispatcher();

        Assert.Equal(2, viewModel.HistoricalSegments.Count);
        Assert.All(viewModel.HistoricalSegments, row => Assert.False(row.IncludeInOverview));
        Assert.All(viewModel.HistoricalSegments, row => Assert.True(row.IsExcluded));
        Assert.Equal("0 of 2", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("00:00:00", viewModel.IncludedObservedDurationLabel);
        Assert.False(viewModel.Overview.HasHistory);
        Assert.True(viewModel.HasHistoricalSegments);
        Assert.False(viewModel.ShowHistoricalEmptyState);
        Assert.True(viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == included.GameplaySessionId).IsExpanded);

        var excludedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId);
        viewModel.ToggleHistoricalSegmentExclusionCommand.Execute(excludedRow);
        DrainDispatcher();

        Assert.True(viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId).IncludeInOverview);
        Assert.False(viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId).IsExcluded);
        Assert.Equal("1 of 2", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("1,000", viewModel.Overview.TotalExperienceLabel);
    }

    [Fact]
    public void Highlight_does_not_enable_delete_selected_checked_rows_do()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100, include: true);
        fixture.Persist(
            fixture.Character,
            experience: 200,
            include: true,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: false));

        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));

        var first = viewModel.HistoricalSegments[0];
        var second = viewModel.HistoricalSegments[1];
        viewModel.SelectHistoricalSegmentCommand.Execute(first);
        Assert.True(first.IsSelected);
        Assert.True(first.IsExpanded);
        Assert.False(first.IsCheckedForDelete);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));

        MarkForDelete(viewModel, first);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));

        MarkForDelete(viewModel, second);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        Assert.Equal(2, viewModel.HistoricalSegments.Count(row => row.IsCheckedForDelete));
        Assert.Same(first, viewModel.SelectedHistoricalSegment);
    }

    [Fact]
    public void Exclude_state_does_not_count_as_delete_selection()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100, include: true);
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: false));
        var row = Assert.Single(viewModel.HistoricalSegments);
        viewModel.SelectHistoricalSegmentCommand.Execute(row);
        viewModel.ToggleHistoricalSegmentExclusionCommand.Execute(row);
        DrainDispatcher();

        var refreshed = Assert.Single(viewModel.HistoricalSegments);
        Assert.True(refreshed.IsExcluded);
        Assert.False(refreshed.IsCheckedForDelete);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void Overview_xaml_keeps_segments_always_visible_with_row_scrolling()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "CoHAnalytics",
            "Workspaces",
            "AnalyticsView.xaml");
        var xaml = File.ReadAllText(path);
        var document = System.Xml.Linq.XDocument.Load(path);
        var xamlNs = document.Root!.Name.Namespace;
        var xNs = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.Contains("x:Name=\"HistoricalCoveragePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AnalyticsPrimaryWorkspace\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "MinHeight=\"{Binding ViewportHeight, RelativeSource={RelativeSource AncestorType=ScrollViewer}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ShowOverviewContent}\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Value=\"{Binding ViewportHeight, RelativeSource={RelativeSource AncestorType=ScrollViewer}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("Property=\"MaxHeight\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoricalPrimaryOverview\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoricalMetricPanels\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoricalSegmentManager\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoricalSegmentRowsScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Overview Segments\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"SELECT\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"EXCLUDE\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsCheckedForDelete, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsExcluded, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleHistoricalSegmentDeleteSelectionCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Delete Selected\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage Segments", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleHistoricalSegmentManager", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHistoricalSegmentManagerExpanded", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Delete\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, xaml.Split("Content=\"Delete Selected\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("x:Name=\"HistoricalOverviewScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"260\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<StackPanel Background=\"{StaticResource Brush.WorkspaceBackground}\">", xaml, StringComparison.Ordinal);

        Assert.Equal("AnalyticsPrimaryWorkspace",
            (string?)document.Root!.Elements(xamlNs + "Grid").Single().Attribute(xNs + "Name"));
        Assert.Single(document.Descendants(xamlNs + "ScrollViewer"));

        var overview = document.Descendants(xamlNs + "Grid")
            .Single(e => (string?)e.Attribute(xNs + "Name") == "HistoricalPrimaryOverview");
        var overviewRows = overview.Element(xamlNs + "Grid.RowDefinitions")!.Elements(xamlNs + "RowDefinition")
            .Select(e => (string)e.Attribute("Height")!).ToArray();
        Assert.Equal(new[] { "Auto", "Auto", "*" }, overviewRows);
        Assert.Equal("1", (string?)document.Descendants(xamlNs + "Grid")
            .Single(e => (string?)e.Attribute(xNs + "Name") == "HistoricalMetricPanels").Attribute("Grid.Row"));
        var manager = document.Descendants(xamlNs + "Border")
            .Single(e => (string?)e.Attribute(xNs + "Name") == "HistoricalSegmentManager");
        Assert.Equal("2", (string?)manager.Attribute("Grid.Row"));
        var rowsViewer = document.Descendants(xamlNs + "ScrollViewer")
            .Single(e => (string?)e.Attribute(xNs + "Name") == "HistoricalSegmentRowsScrollViewer");
        Assert.Equal("3", (string?)rowsViewer.Attribute("Grid.Row"));
        Assert.Contains(rowsViewer.Descendants(xamlNs + "ItemsControl"),
            e => ((string?)e.Attribute("ItemsSource"))?.Contains("HistoricalSegments", StringComparison.Ordinal) == true);
        Assert.Equal("Auto", (string?)rowsViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.DoesNotContain(rowsViewer.Descendants(), e => (string?)e.Attribute("Text") == "SEGMENTS");
        Assert.DoesNotContain(rowsViewer.Descendants(), e => (string?)e.Attribute("Content") == "Delete Selected");
        Assert.DoesNotContain(rowsViewer.Descendants(), e => (string?)e.Attribute("Text") == "DATE / TIME");
        Assert.Contains(manager.Descendants(), e => (string?)e.Attribute("Text") == "SEGMENTS");
        Assert.Contains(manager.Descendants(), e => (string?)e.Attribute("Content") == "Delete Selected");
        Assert.Contains(manager.Descendants(), e => (string?)e.Attribute("Text") == "DATE / TIME");
        Assert.DoesNotContain(overview.Ancestors(xamlNs + "ScrollViewer"), _ => true);

        var coverageIndex = xaml.IndexOf("x:Name=\"HistoricalCoveragePanel\"", StringComparison.Ordinal);
        var metricsIndex = xaml.IndexOf("x:Name=\"HistoricalMetricPanels\"", StringComparison.Ordinal);
        var managerIndex = xaml.IndexOf("x:Name=\"HistoricalSegmentManager\"", StringComparison.Ordinal);
        Assert.True(coverageIndex < metricsIndex);
        Assert.True(metricsIndex < managerIndex);
    }

    [Fact]
    public void Failed_toggle_keeps_persisted_state_and_surfaces_nonfatal_error()
    {
        using var fixture = new Fixture();
        var observation = fixture.Persist(fixture.Character, experience: 100, include: true);
        fixture.ReopenWithFailingUpdates();
        using var viewModel = fixture.CreateViewModel();

        var selected = Assert.Single(viewModel.HistoricalSegments);
        viewModel.SelectHistoricalSegmentCommand.Execute(selected);
        viewModel.ToggleHistoricalSegmentExclusionCommand.Execute(selected);
        DrainDispatcher();

        Assert.True(Assert.Single(viewModel.HistoricalSegments).IncludeInOverview);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);
        Assert.True(viewModel.HasHistoricalSegmentError);
        Assert.NotNull(viewModel.HistoricalSegmentErrorMessage);
        Assert.True(fixture.Repository.GetByCharacter(fixture.Character)
            .Single(item => item.GameplaySessionId == observation.GameplaySessionId)
            .IncludeInOverview);
    }

    [Fact]
    public void Character_switch_replaces_rows_without_stale_history()
    {
        using var fixture = new Fixture();
        var other = fixture.CreateCharacter("Other Hero");
        fixture.Persist(fixture.Character, experience: 100);
        var otherObservation = fixture.Persist(
            other,
            experience: 900,
            session: GameplaySessionId.CreateNew());
        using var viewModel = fixture.CreateViewModel();

        fixture.Viewed.SelectViewedCharacter(fixture.AccountStableId, other);
        DrainDispatcher();

        var row = Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal(otherObservation.GameplaySessionId, row.GameplaySessionId);
        Assert.Equal("Other Hero", row.CharacterLabel);
        Assert.Equal("900", viewModel.Overview.TotalExperienceLabel);
    }

    [Fact]
    public void Account_resolution_honors_anonymity_and_missing_account_falls_back_without_stable_id()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100);
        var missingAccountCharacter = fixture.CreateCharacter("Orphan Hero", "opaque-missing-account-id");
        fixture.Persist(
            missingAccountCharacter,
            experience: 200,
            session: GameplaySessionId.CreateNew());
        using var viewModel = fixture.CreateViewModel();

        Assert.Equal("TestAccount", Assert.Single(viewModel.HistoricalSegments).AccountLabel);

        fixture.Anonymity.SetEnabled(true);
        DrainDispatcher();
        Assert.Equal(new string('█', "TestAccount".Length),
            Assert.Single(viewModel.HistoricalSegments).AccountLabel);

        fixture.Viewed.SelectViewedCharacter("opaque-missing-account-id", missingAccountCharacter);
        DrainDispatcher();
        Assert.Equal("Unknown account", Assert.Single(viewModel.HistoricalSegments).AccountLabel);
    }

    [Fact]
    public void Cancelled_delete_preserves_segment_and_uses_human_readable_anonymized_context()
    {
        using var fixture = new Fixture();
        var observation = fixture.Persist(fixture.Character, experience: 100);
        fixture.Anonymity.SetEnabled(true);
        var confirmation = new RecordingConfirmationService(confirm: false);
        using var viewModel = fixture.CreateViewModel(confirmation);
        var row = Assert.Single(viewModel.HistoricalSegments);

        MarkForDelete(viewModel, row);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        var request = Assert.Single(confirmation.Requests);
        Assert.Equal(1, request.SegmentCount);
        Assert.Equal("Delete 1 selected segment?", request.Headline);
        Assert.Equal(row.CharacterLabel, request.CharacterLabel);
        Assert.Equal(row.AccountLabel, request.AccountLabel);
        Assert.Equal(new string('█', "TestAccount".Length), request.AccountLabel);
        Assert.Equal(row.ConfirmationDateTimeLabel, request.DateTimeLabel);
        Assert.Contains("2026", request.DateTimeLabel, StringComparison.Ordinal);
        Assert.Single(viewModel.HistoricalSegments);
        Assert.Single(fixture.Repository.GetByCharacter(fixture.Character));
        Assert.Equal(observation.GameplaySessionId,
            Assert.Single(fixture.Repository.GetByCharacter(fixture.Character)).GameplaySessionId);
        Assert.True(Assert.Single(viewModel.HistoricalSegments).IsCheckedForDelete);
        Assert.False(viewModel.HasHistoricalSegmentError);
    }

    [Fact]
    public void Confirmed_delete_removes_exact_included_row_and_recalculates_overview()
    {
        using var fixture = new Fixture();
        var deleted = fixture.Persist(fixture.Character, experience: 100);
        var retained = fixture.Persist(
            fixture.Character,
            experience: 900,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        var confirmation = new RecordingConfirmationService(confirm: true);
        using var viewModel = fixture.CreateViewModel(confirmation);
        var deletedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == deleted.GameplaySessionId);
        viewModel.SelectHistoricalSegmentCommand.Execute(deletedRow);
        Assert.True(deletedRow.IsExpanded);
        MarkForDelete(viewModel, deletedRow);

        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Single(confirmation.Requests);
        Assert.Equal(1, confirmation.Requests[0].SegmentCount);
        var row = Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal(retained.GameplaySessionId, row.GameplaySessionId);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("900", viewModel.Overview.TotalExperienceLabel);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        Assert.DoesNotContain(viewModel.HistoricalSegments, item => item.IsExpanded);
        Assert.DoesNotContain(fixture.Repository.GetByCharacter(fixture.Character), item =>
            item.GameplaySessionId == deleted.GameplaySessionId &&
            item.SegmentOrdinal == deleted.SegmentOrdinal);
    }

    [Fact]
    public void Deleting_excluded_row_preserves_included_aggregate()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100, include: true);
        var excluded = fixture.Persist(
            fixture.Character,
            experience: 900,
            include: false,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));
        var excludedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId);

        viewModel.SelectHistoricalSegmentCommand.Execute(excludedRow);
        MarkForDelete(viewModel, excludedRow);
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);
    }

    [Fact]
    public void Deleting_final_row_shows_clean_empty_state()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100);
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));
        var row = Assert.Single(viewModel.HistoricalSegments);
        viewModel.SelectHistoricalSegmentCommand.Execute(row);
        MarkForDelete(viewModel, row);

        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Empty(viewModel.HistoricalSegments);
        Assert.False(viewModel.HasHistoricalSegments);
        Assert.True(viewModel.ShowHistoricalEmptyState);
        Assert.False(viewModel.Overview.HasHistory);
        Assert.Equal("0 of 0", viewModel.OverviewSegmentCountLabel);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void Confirmed_delete_removes_observation_and_durable_artifacts_and_does_not_restore_on_reopen()
    {
        using var fixture = new Fixture();
        var deleted = fixture.Persist(fixture.Character, experience: 100);
        fixture.PersistDurable(deleted);
        var retained = fixture.Persist(
            fixture.Character,
            experience: 900,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        fixture.PersistDurable(retained);
        var chatBytes = File.ReadAllBytes(fixture.SourceChatLogPath);
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));
        var deletedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == deleted.GameplaySessionId);
        MarkForDelete(viewModel, deletedRow);

        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal(retained.GameplaySessionId, Assert.Single(viewModel.HistoricalSegments).GameplaySessionId);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        Assert.DoesNotContain(
            fixture.Store.ListPublishedDirectories(),
            path => path.Contains(deleted.GameplaySessionId.ToString(), StringComparison.Ordinal));
        Assert.Contains(
            fixture.Store.ListPublishedDirectories(),
            path => path.Contains(retained.GameplaySessionId.ToString(), StringComparison.Ordinal));
        Assert.False(File.Exists(fixture.ObservationPath(deleted)));
        Assert.True(File.Exists(fixture.ObservationPath(retained)));
        Assert.Equal(chatBytes, File.ReadAllBytes(fixture.SourceChatLogPath));
        Assert.DoesNotContain(
            viewModel.HistoricalCombat.SegmentChoices,
            choice => choice.Header.GameplaySessionId == deleted.GameplaySessionId);
        Assert.Contains(
            viewModel.HistoricalCombat.SegmentChoices,
            choice => choice.Header.GameplaySessionId == retained.GameplaySessionId);
        Assert.True(Assert.Single(viewModel.HistoricalSegments).IncludeInOverview);

        var reopened = fixture.ReopenReaders();
        Assert.Equal(retained.GameplaySessionId,
            Assert.Single(reopened.Overview.GetSegments(fixture.Character)).Observation.GameplaySessionId);
        Assert.Equal(retained.GameplaySessionId,
            Assert.Single(reopened.Historical.ListHeaders()).GameplaySessionId);
        Assert.Equal(chatBytes, File.ReadAllBytes(fixture.SourceChatLogPath));
    }

    [Fact]
    public void Sequential_confirmed_deletes_remove_every_selected_segment_durably()
    {
        using var fixture = new Fixture();
        var first = fixture.Persist(fixture.Character, experience: 100);
        fixture.PersistDurable(first);
        var second = fixture.Persist(
            fixture.Character,
            experience: 200,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        fixture.PersistDurable(second);
        var third = fixture.Persist(
            fixture.Character,
            experience: 300,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(2));
        fixture.PersistDurable(third);
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));

        var firstRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == first.GameplaySessionId);
        MarkForDelete(viewModel, firstRow);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        var secondRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == second.GameplaySessionId);
        MarkForDelete(viewModel, secondRow);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal(third.GameplaySessionId, Assert.Single(viewModel.HistoricalSegments).GameplaySessionId);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        Assert.True(File.Exists(fixture.ObservationPath(third)));
        Assert.False(File.Exists(fixture.ObservationPath(first)));
        Assert.False(File.Exists(fixture.ObservationPath(second)));
        Assert.DoesNotContain(
            fixture.Store.ListPublishedDirectories(),
            path => path.Contains(first.GameplaySessionId.ToString(), StringComparison.Ordinal)
                || path.Contains(second.GameplaySessionId.ToString(), StringComparison.Ordinal));
        Assert.Contains(
            fixture.Store.ListPublishedDirectories(),
            path => path.Contains(third.GameplaySessionId.ToString(), StringComparison.Ordinal));

        viewModel.SelectHistoricalSegmentCommand.Execute(Assert.Single(viewModel.HistoricalSegments));
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        MarkForDelete(viewModel, Assert.Single(viewModel.HistoricalSegments));
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));

        var reopened = fixture.ReopenReaders();
        Assert.Equal(third.GameplaySessionId,
            Assert.Single(reopened.Overview.GetSegments(fixture.Character)).Observation.GameplaySessionId);
        Assert.Equal(third.GameplaySessionId,
            Assert.Single(reopened.Historical.ListHeaders()).GameplaySessionId);
    }

    [Fact]
    public void Failed_delete_keeps_row_and_aggregate_and_surfaces_nonfatal_error()
    {
        using var fixture = new Fixture();
        var observation = fixture.Persist(fixture.Character, experience: 100);
        fixture.ReopenWithFailingDeletes();
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));

        var row = Assert.Single(viewModel.HistoricalSegments);
        MarkForDelete(viewModel, row);
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal(observation.GameplaySessionId,
            Assert.Single(viewModel.HistoricalSegments).GameplaySessionId);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);
        Assert.True(viewModel.HasHistoricalSegmentError);
        Assert.Equal("1 of 1 segments could not be deleted.", viewModel.HistoricalSegmentErrorMessage);
        Assert.Single(fixture.Repository.GetByCharacter(fixture.Character));
        Assert.True(Assert.Single(viewModel.HistoricalSegments).IsCheckedForDelete);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void Batch_delete_removes_checked_rows_with_one_confirmation_and_leaves_neighbors()
    {
        using var fixture = new Fixture();
        var first = fixture.Persist(fixture.Character, experience: 100);
        fixture.PersistDurable(first);
        var second = fixture.Persist(
            fixture.Character,
            experience: 200,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        fixture.PersistDurable(second);
        var third = fixture.Persist(
            fixture.Character,
            experience: 300,
            include: false,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(2));
        fixture.PersistDurable(third);
        var chatBytes = File.ReadAllBytes(fixture.SourceChatLogPath);
        var confirmation = new RecordingConfirmationService(confirm: true);
        using var viewModel = fixture.CreateViewModel(confirmation);

        var firstRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == first.GameplaySessionId);
        var secondRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == second.GameplaySessionId);
        var thirdRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == third.GameplaySessionId);
        Assert.True(thirdRow.IsExcluded);
        MarkForDelete(viewModel, firstRow, secondRow);

        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Single(confirmation.Requests);
        Assert.Equal(2, confirmation.Requests[0].SegmentCount);
        Assert.Equal("Delete 2 selected segments?", confirmation.Requests[0].Headline);
        var retained = Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal(third.GameplaySessionId, retained.GameplaySessionId);
        Assert.True(retained.IsExcluded);
        Assert.False(retained.IsCheckedForDelete);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        Assert.False(viewModel.HasHistoricalSegmentError);
        Assert.False(File.Exists(fixture.ObservationPath(first)));
        Assert.False(File.Exists(fixture.ObservationPath(second)));
        Assert.True(File.Exists(fixture.ObservationPath(third)));
        Assert.DoesNotContain(
            fixture.Store.ListPublishedDirectories(),
            path => path.Contains(first.GameplaySessionId.ToString(), StringComparison.Ordinal)
                || path.Contains(second.GameplaySessionId.ToString(), StringComparison.Ordinal));
        Assert.Contains(
            fixture.Store.ListPublishedDirectories(),
            path => path.Contains(third.GameplaySessionId.ToString(), StringComparison.Ordinal));
        Assert.Equal(chatBytes, File.ReadAllBytes(fixture.SourceChatLogPath));
        Assert.DoesNotContain(
            viewModel.HistoricalCombat.SegmentChoices,
            choice => choice.Header.GameplaySessionId == first.GameplaySessionId
                || choice.Header.GameplaySessionId == second.GameplaySessionId);
        Assert.Contains(
            viewModel.HistoricalCombat.SegmentChoices,
            choice => choice.Header.GameplaySessionId == third.GameplaySessionId);

        var reopened = fixture.ReopenReaders();
        Assert.Equal(third.GameplaySessionId,
            Assert.Single(reopened.Overview.GetSegments(fixture.Character)).Observation.GameplaySessionId);
        Assert.Equal(third.GameplaySessionId,
            Assert.Single(reopened.Historical.ListHeaders()).GameplaySessionId);
        Assert.Equal(chatBytes, File.ReadAllBytes(fixture.SourceChatLogPath));
    }

    [Fact]
    public void Cancelled_batch_delete_preserves_checks_and_deletes_nothing()
    {
        using var fixture = new Fixture();
        var first = fixture.Persist(fixture.Character, experience: 100);
        var second = fixture.Persist(
            fixture.Character,
            experience: 200,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        var confirmation = new RecordingConfirmationService(confirm: false);
        using var viewModel = fixture.CreateViewModel(confirmation);
        MarkForDelete(
            viewModel,
            viewModel.HistoricalSegments.Single(row => row.GameplaySessionId == first.GameplaySessionId),
            viewModel.HistoricalSegments.Single(row => row.GameplaySessionId == second.GameplaySessionId));

        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal(2, Assert.Single(confirmation.Requests).SegmentCount);
        Assert.Equal(2, viewModel.HistoricalSegments.Count);
        Assert.All(viewModel.HistoricalSegments, row => Assert.True(row.IsCheckedForDelete));
        Assert.Equal(2, fixture.Repository.GetByCharacter(fixture.Character).Count);
        Assert.False(viewModel.HasHistoricalSegmentError);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void Partial_batch_failure_continues_later_rows_and_reports_failure_count()
    {
        using var fixture = new Fixture();
        var first = fixture.Persist(fixture.Character, experience: 100);
        fixture.PersistDurable(first);
        var second = fixture.Persist(
            fixture.Character,
            experience: 200,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        fixture.PersistDurable(second);
        var third = fixture.Persist(
            fixture.Character,
            experience: 300,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(2));
        fixture.PersistDurable(third);
        var inner = new HistoricalSegmentReadService(fixture.Store, fixture.Repository, fixture.CharacterRepository);
        var confirmation = new RecordingConfirmationService(confirm: true);
        using var viewModel = fixture.CreateViewModel(
            confirmation,
            historicalSegmentReader: new SelectiveFailingHistoricalSegmentReader(inner, third.GameplaySessionId));

        MarkForDelete(viewModel, [.. viewModel.HistoricalSegments]);
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Single(confirmation.Requests);
        Assert.Equal(3, confirmation.Requests[0].SegmentCount);
        var remaining = Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal(third.GameplaySessionId, remaining.GameplaySessionId);
        Assert.True(remaining.IsCheckedForDelete);
        Assert.Equal("1 of 3 segments could not be deleted.", viewModel.HistoricalSegmentErrorMessage);
        Assert.False(File.Exists(fixture.ObservationPath(first)));
        Assert.False(File.Exists(fixture.ObservationPath(second)));
        Assert.True(File.Exists(fixture.ObservationPath(third)));
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        Assert.DoesNotContain(
            viewModel.HistoricalCombat.SegmentChoices,
            choice => choice.Header.GameplaySessionId == first.GameplaySessionId
                || choice.Header.GameplaySessionId == second.GameplaySessionId);

        var reopened = fixture.ReopenReaders();
        Assert.Equal(third.GameplaySessionId,
            Assert.Single(reopened.Overview.GetSegments(fixture.Character)).Observation.GameplaySessionId);
        Assert.Equal(third.GameplaySessionId,
            Assert.Single(reopened.Historical.ListHeaders()).GameplaySessionId);
    }

    [Fact]
    public void Empty_history_keeps_manager_and_delete_selection_inert()
    {
        using var fixture = new Fixture();
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));

        Assert.False(viewModel.HasHistoricalSegments);
        Assert.True(viewModel.ShowHistoricalEmptyState);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    private static void MarkForDelete(
        AnalyticsViewModel viewModel,
        params AnalyticsHistoricalSegmentRowViewModel[] rows)
    {
        foreach (var row in rows)
        {
            Assert.False(row.IsCheckedForDelete);
            viewModel.ToggleHistoricalSegmentDeleteSelectionCommand.Execute(row);
            Assert.True(row.IsCheckedForDelete);
        }
    }

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "CoHAnalytics.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-segment-browser",
            Guid.NewGuid().ToString("n"));
        private readonly string _installRoot;

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            _installRoot = HomecomingRuntimeTestSupport.CreateInstallRoot();
            Directory.CreateDirectory(Path.Combine(_installRoot, "accounts", "TestAccount", "Logs"));
            var settings = new SettingsService(Path.Combine(_root, "settings"));
            var installation = new HomecomingInstallationService(settings);
            Assert.True(installation.TryConfigureInstallRoot(_installRoot, out var failureReason), failureReason);
            AccountDiscovery = new HomecomingAccountDiscoveryService(installation);
            AccountStableId = Assert.Single(AccountDiscovery.Discover()).StableId;
            CharacterRepository = new CharacterRepository(new CharacterRepositoryOptions
            {
                DataDirectory = _root
            });
            Character = CreateCharacter("Dawn's Vanguard");
            Repository = new CharacterPerformanceObservationRepository(_root);
            Store = new SegmentStore(_root);
            ReadService = new CharacterHistoricalPerformanceReadService(Repository, CharacterRepository);
            SourceChatLogPath = Path.Combine(_installRoot, "accounts", "TestAccount", "Logs", "chatlog.txt");
            File.WriteAllText(SourceChatLogPath, "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.");
            Viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
                TestGameplaySessionContextSupport.PinnedCharacter(AccountStableId, Character));
            Anonymity = new AccountAnonymityService(StubInternalFeatureGate.Enabled);
        }

        public string AccountStableId { get; }
        public CharacterRecordId Character { get; }
        public CharacterRepository CharacterRepository { get; }
        public CharacterPerformanceObservationRepository Repository { get; private set; }
        public SegmentStore Store { get; }
        public CharacterHistoricalPerformanceReadService ReadService { get; private set; }
        public string SourceChatLogPath { get; }
        public HomecomingAccountDiscoveryService AccountDiscovery { get; }
        public TestGameplaySessionContextSupport.FakeViewedContextService Viewed { get; }
        public AccountAnonymityService Anonymity { get; }

        public CharacterRecordId CreateCharacter(string name, string? accountStableId = null) =>
            Assert.IsType<CharacterRecordId>(CharacterRepository
                .EstablishTrustedFromWelcome(accountStableId ?? AccountStableId, name, Start)
                .RecordId);

        public CharacterPerformanceObservation Persist(
            CharacterRecordId character,
            long experience,
            bool include = true,
            GameplaySessionId? session = null,
            DateTimeOffset? startedAt = null)
        {
            var start = startedAt ?? Start;
            var observation = new CharacterPerformanceObservation
            {
                GameplaySessionId = session ?? GameplaySessionId.CreateNew(),
                SegmentOrdinal = 0,
                CharacterRecordId = character,
                StartedAtUtc = start,
                EndedAtUtc = start.AddMinutes(30),
                IncludeInOverview = include,
                ExperienceGained = experience
            };
            Assert.True(Repository.Persist(observation).IsSuccess);
            return observation;
        }

        public SegmentDraft PersistDurable(CharacterPerformanceObservation observation)
        {
            var engine = new CombatEngine();
            engine.Freeze();
            var projection = engine.Project(new SegmentClockCapture
            {
                CaptureStartUtc = observation.StartedAtUtc,
                CaptureEndUtc = observation.EndedAtUtc,
                AsOfUtc = observation.EndedAtUtc
            });
            var draft = new SegmentDraft
            {
                GameplaySessionId = observation.GameplaySessionId,
                SegmentOrdinal = observation.SegmentOrdinal,
                CharacterRecordId = observation.CharacterRecordId,
                AccountStableId = AccountStableId,
                CaptureStartUtc = observation.StartedAtUtc,
                CaptureEndUtc = observation.EndedAtUtc,
                FinalizedAtUtc = observation.EndedAtUtc,
                Aggregates = projection,
                Spine = [],
                Coverage = new SegmentCoverageDescriptor
                {
                    LogicalEventCount = 0,
                    DuplicateOccurrencesIgnored = 0,
                    RetainedSpineEventCount = 0,
                    SpineRetentionLimit = SegmentSpineLimits.MaxRetainedLogicalEvents,
                    SpineTruncated = false,
                    Replay = LosslessReplayCoverageMatrix.ForCapture(false, true, false)
                }
            };
            Assert.Equal(SegmentPersistOutcome.Persisted, Store.Persist(draft).Outcome);
            return draft;
        }

        public string ObservationPath(CharacterPerformanceObservation observation) =>
            Path.Combine(
                Repository.ObservationDirectory,
                CharacterPerformanceObservationRepository.BuildFileName(
                    observation.GameplaySessionId,
                    observation.SegmentOrdinal));

        public (CharacterHistoricalPerformanceReadService Overview, HistoricalSegmentReadService Historical) ReopenReaders()
        {
            var observations = new CharacterPerformanceObservationRepository(_root);
            return (
                new CharacterHistoricalPerformanceReadService(observations, CharacterRepository),
                new HistoricalSegmentReadService(new SegmentStore(_root), observations, CharacterRepository));
        }

        public void ReopenWithFailingUpdates()
        {
            Repository = new CharacterPerformanceObservationRepository(
                _root,
                (_, _) => throw new IOException("Simulated replacement failure."));
            ReadService = new CharacterHistoricalPerformanceReadService(Repository, CharacterRepository);
        }

        public void ReopenWithFailingDeletes()
        {
            Repository = new CharacterPerformanceObservationRepository(
                _root,
                (temporaryPath, finalPath) => File.Move(temporaryPath, finalPath, overwrite: true),
                _ => throw new IOException("Simulated deletion failure."));
            ReadService = new CharacterHistoricalPerformanceReadService(Repository, CharacterRepository);
        }

        public AnalyticsViewModel CreateViewModel(
            IHistoricalSegmentDeleteConfirmationService? confirmationService = null,
            ISegmentReportService? reportService = null,
            IHistoricalSegmentReader? historicalSegmentReader = null)
        {
            var identity = new FakeIdentityReadService();
            return new AnalyticsViewModel(
                new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
                new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
                identity,
                new GameplaySessionContextResolver(identity, Viewed),
                Viewed,
                Anonymity,
                ReadService,
                Repository,
                CharacterRepository,
                AccountDiscovery,
                confirmationService,
                reportService,
                historicalSegmentReader ?? new HistoricalSegmentReadService(Store, Repository, CharacterRepository));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                Directory.Delete(_installRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class SelectiveFailingHistoricalSegmentReader(
        IHistoricalSegmentReader inner,
        GameplaySessionId failingSession) : IHistoricalSegmentReader
    {
        public IReadOnlyList<HistoricalSegmentHeader> ListHeaders(HistoricalSegmentQuery? query = null) =>
            inner.ListHeaders(query);

        public HistoricalLoadResult TryLoad(string segmentId, HistoricalLoadOptions? options = null) =>
            inner.TryLoad(segmentId, options);

        public HistoricalLoadResult TryLoad(
            GameplaySessionId gameplaySessionId,
            int segmentOrdinal,
            HistoricalLoadOptions? options = null) =>
            inner.TryLoad(gameplaySessionId, segmentOrdinal, options);

        public SegmentDeleteResult Delete(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
            gameplaySessionId == failingSession
                ? new() { Outcome = SegmentDeleteOutcome.PersistenceFailed, Detail = "Simulated deletion failure." }
                : inner.Delete(gameplaySessionId, segmentOrdinal);
    }

    private sealed class RecordingConfirmationService(bool confirm)
        : IHistoricalSegmentDeleteConfirmationService
    {
        public List<HistoricalSegmentDeleteConfirmationRequest> Requests { get; } = [];

        public bool ConfirmDelete(HistoricalSegmentDeleteConfirmationRequest request)
        {
            Requests.Add(request);
            return confirm;
        }
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current =>
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    private static readonly DateTimeOffset Start =
        new(2026, 8, 24, 0, 14, 0, TimeSpan.Zero);
}
