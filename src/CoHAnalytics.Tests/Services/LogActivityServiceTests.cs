using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class LogActivityServiceTests
{
    [Theory]
    [InlineData("chatlog 2026-08-04.txt")]
    [InlineData("CHATLOG 2026-08-04.TXT")]
    [InlineData("ChatLog 2026-12-31.Txt")]
    public void Valid_daily_chat_log_names_are_recognized(string fileName)
    {
        Assert.True(LogActivityService.TryParseChatLogDate(fileName, out var logDate));
        Assert.Equal(2026, logDate.Year);
    }

    [Theory]
    [InlineData("chatlog.txt")]
    [InlineData("chatlog 2026-8-4.txt")]
    [InlineData("chatlog 2026-13-01.txt")]
    [InlineData("chatlog 2026-02-30.txt")]
    [InlineData("chatlog 2026-08-04.txt.tmp")]
    [InlineData("chatlog 2026-08-04.tmp")]
    [InlineData("~chatlog 2026-08-04.txt")]
    [InlineData("notes.txt")]
    [InlineData("client.log")]
    [InlineData("")]
    public void Invalid_names_are_rejected(string fileName) =>
        Assert.False(LogActivityService.TryParseChatLogDate(fileName, out _));

    [Fact]
    public async Task Same_file_name_under_two_accounts_yields_two_source_ids()
    {
        using var environment = new LogActivityTestEnvironment();
        var first = environment.AddAccount("Alpha");
        var second = environment.AddAccount("Beta");
        environment.WriteLog(first, environment.Today);
        environment.WriteLog(second, environment.Today);

        using var service = environment.CreateService();
        await service.StartAsync();

        var candidates = service.Current.Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal($"chatlog {environment.Today:yyyy-MM-dd}.txt", candidate.FileName));
        Assert.Equal(2, candidates.Select(candidate => candidate.SourceId.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Account_without_logs_folder_is_valid_and_produces_no_candidates_or_failures()
    {
        using var environment = new LogActivityTestEnvironment();
        environment.AddAccount("NoLogs", createLogsFolder: false);

        using var service = environment.CreateService();
        await service.StartAsync();

        Assert.Empty(service.Current.Candidates);
        Assert.Equal(1, service.Current.ObservedAccountCount);
        Assert.Equal(0, service.Current.LogsFolderCount);
        Assert.Null(service.LastScanFailureMessage);

        var diagnostics = service.GetDiagnostics();
        Assert.Empty(diagnostics.RecentScanFailures);
        Assert.False(Assert.Single(diagnostics.Accounts).LogsFolderPresent);
    }

    [Fact]
    public async Task Empty_logs_folder_produces_no_candidates()
    {
        using var environment = new LogActivityTestEnvironment();
        environment.AddAccount("Empty");

        using var service = environment.CreateService();
        await service.StartAsync();

        Assert.Empty(service.Current.Candidates);
        Assert.Equal(1, service.Current.LogsFolderCount);
    }

    [Fact]
    public async Task Unrelated_files_are_ignored_and_historical_logs_are_ordered_deterministically()
    {
        using var environment = new LogActivityTestEnvironment();
        var beta = environment.AddAccount("Beta");
        var alpha = environment.AddAccount("Alpha");

        var older = environment.Today.AddDays(-3);
        var newer = environment.Today.AddDays(-1);
        environment.WriteLog(alpha, older);
        environment.WriteLog(alpha, newer);
        environment.WriteLog(beta, older);
        environment.WriteUnrelatedFile(alpha, "notes.txt");
        environment.WriteUnrelatedFile(alpha, "chatlog 2026-13-40.txt");

        using var service = environment.CreateService();
        await service.StartAsync();

        Assert.Equal(
            [
                ("Alpha", newer),
                ("Alpha", older),
                ("Beta", older)
            ],
            service.Current.Candidates
                .Select(candidate => (candidate.AccountDisplayName, candidate.LogDate))
                .ToArray());
    }

    [Fact]
    public async Task First_observation_of_an_existing_file_never_reports_growth()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "already has content");
        environment.WriteLog(account, environment.Today.AddDays(-2), "older content");

        using var service = environment.CreateService();
        await service.StartAsync();

        var today = Single(service, environment.Today);
        var historical = Single(service, environment.Today.AddDays(-2));

        Assert.Equal(LogSourceActivityState.Waiting, today.ActivityState);
        Assert.Equal(LogSourceActivityState.Historical, historical.ActivityState);
        Assert.Equal(LogSourceChangeKind.Discovered, today.LastChangeKind);
        Assert.All(service.Current.Candidates, candidate =>
        {
            Assert.Null(candidate.FirstGrowthAt);
            Assert.Null(candidate.LastGrowthAt);
            Assert.False(candidate.HasObservedGrowth);
            Assert.Equal(candidate.Length, candidate.PreviousLength);
        });
        Assert.Equal(0, service.Current.GrowingCount);
    }

    [Fact]
    public async Task Observed_append_reports_growth_and_bumps_the_revision_once()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "start");

        using var service = environment.CreateService();
        await service.StartAsync();
        var discoveredRevision = service.Current.Revision;

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today, "appended");
        await service.ScanAsync();

        var grown = Single(service, environment.Today);
        Assert.Equal(LogSourceActivityState.Growing, grown.ActivityState);
        Assert.Equal(LogSourceChangeKind.Grew, grown.LastChangeKind);
        Assert.Equal(environment.Time.GetUtcNow(), grown.LastGrowthAt);
        Assert.Equal(environment.Time.GetUtcNow(), grown.FirstGrowthAt);
        Assert.True(grown.Length > grown.PreviousLength);
        Assert.Equal(1, service.Current.GrowingCount);
        Assert.Equal(discoveredRevision + 1, service.Current.Revision);
        Assert.Equal(service.Current.LastGrowthAt, grown.LastGrowthAt);
    }

    [Fact]
    public async Task Unchanged_scans_do_not_churn_revisions_or_raise_events()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "start");

        using var service = environment.CreateService();
        await service.StartAsync();
        environment.Append(account, environment.Today);
        await service.ScanAsync();

        // The first scan after growth still changes the observed transition to Unchanged.
        await service.ScanAsync();
        var settledRevision = service.Current.Revision;

        var raised = 0;
        service.ActivityChanged += (_, _) => raised++;

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        await service.ScanAsync();
        environment.Time.Advance(TimeSpan.FromSeconds(1));
        await service.ScanAsync();

        Assert.Equal(settledRevision, service.Current.Revision);
        Assert.Equal(0, raised);
        Assert.True(service.GetDiagnostics().SuppressedNoOpScanCount >= 2);
        Assert.Equal(environment.Time.GetUtcNow(), service.Current.ObservedAt);
    }

    [Fact]
    public async Task Previously_growing_source_becomes_inactive_after_the_configured_threshold()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "start");

        using var service = environment.CreateService(inactivityThreshold: TimeSpan.FromSeconds(10));
        await service.StartAsync();
        environment.Append(account, environment.Today);
        await service.ScanAsync();
        Assert.Equal(LogSourceActivityState.Growing, Single(service, environment.Today).ActivityState);

        environment.Time.Advance(TimeSpan.FromSeconds(9));
        await service.ScanAsync();
        Assert.Equal(LogSourceActivityState.Growing, Single(service, environment.Today).ActivityState);

        environment.Time.Advance(TimeSpan.FromSeconds(2));
        await service.ScanAsync();

        var inactive = Single(service, environment.Today);
        Assert.Equal(LogSourceActivityState.Inactive, inactive.ActivityState);
        Assert.Equal(1, service.Current.InactiveCount);

        // Inactivity is an observation, not a claim that chat logging was turned off.
        Assert.True(inactive.HasObservedGrowth);
        Assert.NotNull(inactive.LastGrowthAt);
        Assert.True(inactive.Exists);
    }

    [Fact]
    public async Task Idle_current_daily_file_stays_waiting_rather_than_claiming_logging_is_disabled()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "existing");

        using var service = environment.CreateService(inactivityThreshold: TimeSpan.FromSeconds(5));
        await service.StartAsync();

        environment.Time.Advance(TimeSpan.FromMinutes(30));
        await service.ScanAsync();

        Assert.Equal(LogSourceActivityState.Waiting, Single(service, environment.Today).ActivityState);
        Assert.Equal(0, service.Current.InactiveCount);
        Assert.Equal(0, service.Current.GrowingCount);
    }

    [Fact]
    public async Task New_daily_log_is_reported_as_created_and_both_sources_remain_represented()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        var yesterday = environment.Today.AddDays(-1);
        environment.WriteLog(account, yesterday, "yesterday");

        using var service = environment.CreateService();
        await service.StartAsync();
        Assert.Single(service.Current.Candidates);

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.WriteLog(account, environment.Today);
        await service.ScanAsync();

        var created = Single(service, environment.Today);
        Assert.Equal(LogSourceChangeKind.Created, created.LastChangeKind);
        Assert.Equal(LogSourceActivityState.Waiting, created.ActivityState);
        Assert.Equal(2, service.Current.CandidateCount);

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today);
        await service.ScanAsync();

        Assert.Equal(LogSourceActivityState.Growing, Single(service, environment.Today).ActivityState);
        Assert.Equal(2, service.Current.CandidateCount);
        Assert.NotNull(Single(service, yesterday));
    }

    [Fact]
    public async Task Newer_growing_source_is_marked_a_rollover_candidate_when_the_older_goes_inactive()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        var yesterday = environment.Today.AddDays(-1);
        environment.WriteLog(account, yesterday, "yesterday");

        using var service = environment.CreateService(inactivityThreshold: TimeSpan.FromSeconds(10));
        await service.StartAsync();

        environment.Append(account, yesterday);
        await service.ScanAsync();
        var olderGrowthAt = Single(service, yesterday).LastGrowthAt;
        Assert.Equal(LogSourceActivityState.Growing, Single(service, yesterday).ActivityState);

        environment.Time.Advance(TimeSpan.FromSeconds(30));
        environment.WriteLog(account, environment.Today);
        await service.ScanAsync();
        Assert.Equal(LogSourceActivityState.Inactive, Single(service, yesterday).ActivityState);

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today);
        await service.ScanAsync();

        var newer = Single(service, environment.Today);
        var older = Single(service, yesterday);

        Assert.True(newer.IsRolloverCandidate);
        Assert.False(older.IsRolloverCandidate);
        Assert.Equal(older.SourceId.Value, newer.RolloverPredecessorSourceId);
        Assert.NotNull(newer.RolloverReason);
        Assert.Equal(1, service.Current.RolloverCandidateCount);

        // Both sources stay represented, and the facts a later decision needs are present.
        Assert.Equal(2, service.Current.CandidateCount);
        Assert.Equal(older.AccountStableId, newer.AccountStableId);
        Assert.True(newer.LogDate > older.LogDate);
        Assert.Equal(olderGrowthAt, older.LastGrowthAt);
        Assert.NotNull(newer.FirstGrowthAt);
        Assert.True(newer.FirstGrowthAt >= older.LastGrowthAt);
    }

    [Fact]
    public async Task Growing_source_in_a_different_account_is_not_a_rollover_candidate()
    {
        using var environment = new LogActivityTestEnvironment();
        var alpha = environment.AddAccount("Alpha");
        var beta = environment.AddAccount("Beta");
        var yesterday = environment.Today.AddDays(-1);
        environment.WriteLog(alpha, yesterday, "yesterday");
        environment.WriteLog(beta, environment.Today);

        using var service = environment.CreateService(inactivityThreshold: TimeSpan.FromSeconds(10));
        await service.StartAsync();

        environment.Append(alpha, yesterday);
        await service.ScanAsync();

        environment.Time.Advance(TimeSpan.FromSeconds(30));
        environment.Append(beta, environment.Today);
        await service.ScanAsync();

        Assert.Equal(LogSourceActivityState.Inactive, Single(service, yesterday).ActivityState);
        Assert.Equal(LogSourceActivityState.Growing, Single(service, environment.Today).ActivityState);
        Assert.All(service.Current.Candidates, candidate => Assert.False(candidate.IsRolloverCandidate));
        Assert.Equal(0, service.Current.RolloverCandidateCount);
    }

    [Fact]
    public async Task Length_decrease_is_reported_as_truncation_and_not_as_replacement()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "a reasonably long first line");

        using var service = environment.CreateService();
        await service.StartAsync();
        environment.Append(account, environment.Today, "and more content still");
        await service.ScanAsync();
        var grownLength = Single(service, environment.Today).Length;

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Truncate(account, environment.Today, "s");
        await service.ScanAsync();

        var truncated = Single(service, environment.Today);
        Assert.Equal(LogSourceActivityState.Truncated, truncated.ActivityState);
        Assert.Equal(LogSourceChangeKind.Truncated, truncated.LastChangeKind);
        Assert.True(truncated.IsTruncated);
        Assert.False(truncated.IsReplaced);
        Assert.Null(truncated.ReplacementEvidence);
        Assert.Equal(grownLength, truncated.PreviousLength);
        Assert.Equal(1, truncated.Length);
        Assert.Equal(1, service.Current.TruncatedCount);

        var diagnostics = Assert.Single(service.GetDiagnostics().Sources);
        Assert.Equal(grownLength, diagnostics.PreviousLength);
        Assert.Equal(1, diagnostics.CurrentLength);
        Assert.True(diagnostics.IsTruncated);
        Assert.False(diagnostics.IsReplaced);
    }

    [Fact]
    public async Task Observed_disappearance_then_reappearance_is_reported_as_replacement_with_a_new_identity()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "original");

        using var service = environment.CreateService();
        await service.StartAsync();
        environment.Append(account, environment.Today);
        await service.ScanAsync();
        var originalSourceId = Single(service, environment.Today).SourceId.Value;

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Delete(account, environment.Today);
        await service.ScanAsync();

        var missing = Single(service, environment.Today);
        Assert.Equal(LogSourceActivityState.Unavailable, missing.ActivityState);
        Assert.Equal(LogSourceChangeKind.Disappeared, missing.LastChangeKind);
        Assert.False(missing.Exists);
        Assert.NotNull(missing.LastGrowthAt);
        Assert.Equal(originalSourceId, missing.SourceId.Value);

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.WriteLog(account, environment.Today, "recreated");
        await service.ScanAsync();

        var replaced = Single(service, environment.Today);
        Assert.Equal(LogSourceActivityState.Replaced, replaced.ActivityState);
        Assert.Equal(LogSourceChangeKind.Replaced, replaced.LastChangeKind);
        Assert.True(replaced.IsReplaced);
        Assert.NotNull(replaced.ReplacementEvidence);
        Assert.Equal(1, replaced.SourceId.IdentityGeneration);
        Assert.NotEqual(originalSourceId, replaced.SourceId.Value);
        Assert.Null(replaced.FirstGrowthAt);
        Assert.True(replaced.Exists);

        // Recreation recovers: the new identity can be observed growing.
        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today);
        await service.ScanAsync();
        Assert.Equal(LogSourceActivityState.Growing, Single(service, environment.Today).ActivityState);
    }

    [Fact]
    public async Task Changed_creation_timestamp_is_reported_as_replacement()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        var path = environment.WriteLog(account, environment.Today, "original");

        using var service = environment.CreateService();
        await service.StartAsync();
        var originalSourceId = Single(service, environment.Today).SourceId.Value;

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        File.SetCreationTimeUtc(path, File.GetCreationTimeUtc(path).AddMinutes(-5));
        await service.ScanAsync();

        var replaced = Single(service, environment.Today);
        Assert.True(replaced.IsReplaced);
        Assert.Contains("Creation timestamp changed", replaced.ReplacementEvidence);
        Assert.NotEqual(originalSourceId, replaced.SourceId.Value);
        Assert.Equal(1, service.Current.ReplacedCount);
    }

    [Fact]
    public void Diagnostics_document_the_replacement_detection_limitation()
    {
        using var environment = new LogActivityTestEnvironment();
        using var service = environment.CreateService();

        var diagnostics = service.GetDiagnostics();
        Assert.Equal("PollingOnly", diagnostics.ObservationMode);
        Assert.Contains("tunneling", diagnostics.ReplacementDetectionLimitation);
        Assert.Contains("no native interop", diagnostics.ReplacementDetectionLimitation);
    }

    [Fact]
    public async Task Disappeared_historical_source_is_retained_with_its_previous_observations()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        var older = environment.Today.AddDays(-2);
        environment.WriteLog(account, older, "history");

        using var service = environment.CreateService();
        await service.StartAsync();
        var firstObservedAt = Single(service, older).FirstObservedAt;

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Delete(account, older);
        await service.ScanAsync();

        var retained = Single(service, older);
        Assert.False(retained.Exists);
        Assert.Equal(LogSourceActivityState.Unavailable, retained.ActivityState);
        Assert.Equal("File no longer exists at its observed path.", retained.UnavailableReason);
        Assert.Equal(firstObservedAt, retained.FirstObservedAt);
        Assert.Equal(1, service.Current.UnavailableCount);
        Assert.Equal(1, service.Current.CandidateCount);
    }

    [Fact]
    public async Task Multiple_accounts_and_multiple_sources_are_all_reported_without_a_primary()
    {
        using var environment = new LogActivityTestEnvironment();
        var alpha = environment.AddAccount("Alpha");
        var beta = environment.AddAccount("Beta");
        var yesterday = environment.Today.AddDays(-1);

        environment.WriteLog(alpha, environment.Today, "a");
        environment.WriteLog(alpha, yesterday, "b");
        environment.WriteLog(beta, environment.Today, "c");

        using var service = environment.CreateService();
        await service.StartAsync();

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(alpha, environment.Today);
        environment.Append(alpha, yesterday);
        environment.Append(beta, environment.Today);
        await service.ScanAsync();

        Assert.Equal(3, service.Current.CandidateCount);
        Assert.Equal(3, service.Current.GrowingCount);
        Assert.Equal(2, service.Current.ObservedAccountCount);
        Assert.Equal(2, service.Current.LogsFolderCount);
        Assert.Equal(
            2,
            service.Current.Candidates
                .Select(candidate => candidate.AccountStableId)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void Candidate_model_exposes_no_ownership_selection_or_session_concepts()
    {
        var forbidden = new[]
        {
            "Owner", "Owned", "Claim", "Assigned", "Assignment", "Selected", "Selection",
            "Context", "Session", "Character", "Parser", "IsPrimary", "IsActiveLog"
        };

        var propertyNames = typeof(LogSourceCandidate)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(LogActivitySnapshot).GetProperties().Select(property => property.Name))
            .ToArray();

        var offenders = propertyNames
            .Where(name => forbidden.Any(term => name.Contains(term, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task Start_and_stop_are_idempotent_and_support_restart()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today);

        using var service = environment.CreateService();

        await service.StartAsync();
        await service.StartAsync();
        Assert.True(service.IsRunning);
        Assert.Equal(1, service.GetDiagnostics().ScanCount);

        await service.StopAsync();
        await service.StopAsync();
        Assert.False(service.IsRunning);

        await service.StartAsync();
        Assert.True(service.IsRunning);
        Assert.Equal(2, service.GetDiagnostics().ScanCount);
    }

    [Fact]
    public async Task Scan_after_stop_neither_observes_nor_raises_events()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today);

        using var service = environment.CreateService();
        await service.StartAsync();
        await service.StopAsync();

        var raised = 0;
        service.ActivityChanged += (_, _) => raised++;

        environment.Append(account, environment.Today);
        await service.ScanAsync();

        Assert.Equal(1, service.GetDiagnostics().ScanCount);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Overlapping_scan_requests_collapse_into_a_single_follow_up_pass()
    {
        using var environment = new LogActivityTestEnvironment();
        environment.AddAccount("Alpha");

        using var service = environment.CreateService();
        await service.StartAsync();

        var accounts = environment.Accounts;
        var providerCalls = 0;
        using var release = new ManualResetEventSlim(false);

        environment.AccountsProvider = () =>
        {
            Interlocked.Increment(ref providerCalls);

            // Stays signaled once set, so the follow-up pass proceeds immediately.
            release.Wait(TimeSpan.FromSeconds(5));
            return accounts;
        };

        var inFlight = Task.Run(() => service.ScanAsync());
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref providerCalls) >= 1, TimeSpan.FromSeconds(5)));

        // Every request arriving while a scan is in flight collapses into one follow-up pass.
        for (var index = 0; index < 5; index++)
        {
            await service.ScanAsync();
        }

        release.Set();
        await inFlight;

        Assert.Equal(2, Volatile.Read(ref providerCalls));
        Assert.Equal(3, service.GetDiagnostics().ScanCount);
    }

    [Fact]
    public async Task Change_event_is_raised_without_holding_the_state_lock()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "start");

        using var service = environment.CreateService();
        await service.StartAsync();

        var readFromAnotherThread = false;
        service.ActivityChanged += (_, _) =>
        {
            // Would deadlock if the publication lock were still held on the raising thread.
            readFromAnotherThread = Task.Run(() =>
            {
                _ = service.Current;
                _ = service.GetDiagnostics();
                return true;
            }).Wait(TimeSpan.FromSeconds(5));
        };

        environment.Append(account, environment.Today);
        await service.ScanAsync();

        Assert.True(readFromAnotherThread);
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_stops_observation()
    {
        using var environment = new LogActivityTestEnvironment();
        environment.AddAccount("Alpha");

        var service = environment.CreateService(enablePolling: true);
        await service.StartAsync();
        var scanCountAtDisposal = service.GetDiagnostics().ScanCount;

        service.Dispose();
        service.Dispose();

        Assert.False(service.IsRunning);

        // A scan requested after disposal is a no-op rather than a failure.
        await service.ScanAsync();
        Assert.True(service.GetDiagnostics().ScanCount >= scanCountAtDisposal);
    }

    private static LogSourceCandidate Single(LogActivityService service, DateOnly logDate) =>
        Assert.Single(service.Current.Candidates, candidate => candidate.LogDate == logDate);
}
