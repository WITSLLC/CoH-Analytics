using System.Net;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class SegmentReportTests
{
    [Fact]
    public void Complete_document_formats_only_supported_metrics_without_changing_projection()
    {
        var projection = CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.Available(new(12345)),
                    HealingDealt = Metric<CombatScaledAmount>.Incomplete(new(6789), coverage: new() { LowerBound = true }),
                    Accuracy = MetricRef<CombatAccuracyScopeSnapshot>.Available(new() { Hits = 3, Misses = 1, Attempts = 4 })
                }
            },
            Clock = SegmentClock.Empty with
            {
                WallClockDamagePerSecondHundredths = Metric<long>.Available(9876, denominator: RateDenominatorKind.WallClock),
                WallClockDuration = Metric<TimeSpan>.Available(TimeSpan.FromMinutes(10))
            }
        };
        var before = JsonSerializer.Serialize(projection);
        var html = new HtmlReportRenderer().Render(Segment(projection));
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.EndsWith("</body></html>", html);
        Assert.Contains("<span>Total Damage</span><strong>123.45</strong>", html);
        Assert.Contains("<span>Healing Dealt</span><strong>67.89</strong><small class='metric-note'>Partial — some activity may not have been captured.</small>", html);
        Assert.Contains("<span>Session DPS</span><strong>98.76</strong><small class='metric-note'>Based on total elapsed session time.</small>", html); // no recalculation
        Assert.DoesNotContain("Capture-wall denominator", html);
        Assert.DoesNotContain("Lower bound", html);
        Assert.Contains(WebUtility.HtmlEncode("3 hits · 1 misses · 4 attempts"), html);
        Assert.DoesNotContain("75%", html);
        Assert.DoesNotContain("Active combat", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Active DPS", html);
        Assert.DoesNotContain("NotCaptured", html);
        Assert.DoesNotContain("Unsupported", html);
        Assert.DoesNotContain("CoverageInfo {", html);
        Assert.DoesNotContain("DirectObserved", html);
        Assert.Equal(before, JsonSerializer.Serialize(projection));
    }

    [Fact]
    public void Proc_incompleteness_is_explained_once_in_player_facing_language()
    {
        var coverage = new CoverageInfo { LowerBound = true, UnidentifiedProcSource = true };
        var p = CombatAnalyticsProjection.Empty with
        {
            Attribution = CombatProcAttributionSummary.Empty with
            {
                ProcDamage = Metric<CombatScaledAmount>.Incomplete(new(1200), coverage: coverage),
                ProcContributionHundredths = Metric<long>.Incomplete(315, coverage: coverage)
            }
        };
        var html = new HtmlReportRenderer().Render(Segment(p));
        const string friendly = "Partial — some proc damage could not be identified by source.";
        Assert.Contains(friendly, html);
        Assert.Equal(1, Occurrences(html, friendly));
        Assert.DoesNotContain("Lower bound · Unidentified proc source", html);
        Assert.DoesNotContain("Unidentified proc source</small>", html);
    }

    [Fact]
    public void Outgoing_damage_chart_modes_share_one_authoritative_source_dataset()
    {
        const string hostile = "Pseudo</script><script>alert('x')</script>";
        var p = CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with { DamageDealt = Metric<CombatScaledAmount>.Available(new(6000)) }
            },
            Powers =
            [
                Outgoing(hostile, 3000),
                Outgoing("Reactive Interface", 2000),
                Outgoing("Proc: Chance for Energy Damage", 1000)
            ]
        };
        var html = new HtmlReportRenderer().Render(Segment(p));

        Assert.Contains("Outgoing damage sources ranked by observed damage", html);
        Assert.DoesNotContain("Outgoing powers ranked", html);
        Assert.Contains(">Damage Source</th>", html);
        Assert.Contains("data-chart-mode='donut'", html);
        Assert.Contains("data-chart-mode='pie'", html);
        Assert.Contains("class='active' data-chart-mode='bar' aria-pressed='true'", html);
        Assert.Equal(1, Occurrences(html, "aria-pressed='true'"));
        Assert.Equal(1, Occurrences(html, "id='damage-chart-viewport'"));
        Assert.Contains("style='--chart-height:300px;--chart-mobile-height:406px'", html);
        Assert.DoesNotContain("data-chart-panel", html);
        Assert.DoesNotContain("<div class='bars'>", html);
        Assert.DoesNotContain("<svg class='radial-chart'", html);
        Assert.Contains("const renderDamageChart=mode=>{", html);
        Assert.Contains("host.replaceChildren();", html);
        Assert.Contains("renderRadial(host,mode)", html);
        Assert.Contains("setMode('bar');", html);
        Assert.Equal(1, Occurrences(html, "<script type='application/json' id='outgoing-damage-data'>"));
        Assert.Equal(1, Occurrences(html, "data-chart-source='outgoing-damage-data'"));

        var jsonStart = html.IndexOf("<script type='application/json' id='outgoing-damage-data'>", StringComparison.Ordinal)
            + "<script type='application/json' id='outgoing-damage-data'>".Length;
        var jsonEnd = html.IndexOf("</script>", jsonStart, StringComparison.Ordinal);
        using var data = JsonDocument.Parse(html[jsonStart..jsonEnd]);
        var rows = data.RootElement.EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal([hostile, "Reactive Interface", "Proc: Chance for Energy Damage"],
            rows.Select(r => r.GetProperty("Name").GetString()!).ToArray());
        Assert.Equal([3000L, 2000L, 1000L], rows.Select(r => r.GetProperty("DamageHundredths").GetInt64()).ToArray());
        Assert.DoesNotContain(rows, r => r.GetProperty("Name").GetString() == "Other");
        Assert.DoesNotContain("% Total", html);
        Assert.DoesNotContain(hostile, html);
        Assert.Contains(WebUtility.HtmlEncode(hostile), html);
    }

    [Fact]
    public void Shared_chart_viewport_height_scales_with_the_source_count()
    {
        var powers = Enumerable.Range(1, 12).Select(i => Outgoing($"Source {i}", i * 100L)).ToArray();
        var html = new HtmlReportRenderer().Render(Segment(CombatAnalyticsProjection.Empty with { Powers = powers }));
        Assert.Contains("style='--chart-height:544px;--chart-mobile-height:685px'", html);
        Assert.Equal(1, Occurrences(html, "id='damage-chart-viewport'"));
    }

    [Fact]
    public void NotCaptured_Unsupported_and_empty_sections_are_omitted()
    {
        var p = CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.NotCaptured(),
                    DamageReceived = Metric<CombatScaledAmount>.Unsupported()
                }
            }
        };
        var html = new HtmlReportRenderer().Render(Segment(p));
        Assert.DoesNotContain("data-section='summary'", html);
        Assert.DoesNotContain("data-section='offense'", html);
        Assert.DoesNotContain("data-section='survivability'", html);
        Assert.DoesNotContain("NotCaptured", html);
        Assert.DoesNotContain("Unsupported", html);
    }

    [Fact]
    public void Empty_optional_power_columns_are_omitted_and_dynamic_text_is_escaped()
    {
        const string hostile = "<script>alert(\"x\")</script>&'";
        var p = CombatAnalyticsProjection.Empty with
        {
            Powers = [
                new()
                {
                    Scope = CombatAnalyticsScope.Self, Direction = CombatAnalyticsDirection.Outgoing, PowerName = hostile,
                    DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(1000)), DamageMagnitude = new(1000), EventCount = 2
                }],
            Targets = [new() { NormalizedTargetName = hostile, DamageDealt = new(500), EventCount = 1 }],
            Attribution = CombatProcAttributionSummary.Empty with
            {
                ByParent = [new()
                {
                    Mode = ProcAttributionMode.BuildConfirmed, ParentPowerName = hostile, ExactProcIdentity = hostile,
                    ProcDamage = new(200), ProcDamageMetric = Metric<CombatScaledAmount>.Available(new(200)), EventCount = 1
                }]
            }
        };
        var segment = Segment(p);
        segment = segment with { Header = segment.Header with { CharacterDisplayNameAtCapture = hostile, Archetype = hostile } };
        var html = new HtmlReportRenderer().Render(segment);
        Assert.DoesNotContain(hostile, html);
        Assert.Contains(WebUtility.HtmlEncode(hostile), html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains(">Damage</th>", html);
        Assert.Contains(">Events</th>", html);
        Assert.DoesNotContain(">Direct</th>", html);
        Assert.DoesNotContain(">DoT</th>", html);
        Assert.DoesNotContain(">Observed Activations</th>", html);
        Assert.DoesNotContain(">% Total</th>", html);
    }

    [Fact]
    public void Actual_application_logo_is_embedded_and_placeholder_is_absent()
    {
        var html = new HtmlReportRenderer().Render(Segment());
        const string prefix = "src='data:image/png;base64,";
        var start = html.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += prefix.Length;
        var end = html.IndexOf('\'', start);
        var embedded = Convert.FromBase64String(html[start..end]);
        var expected = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoHAnalytics", "Assets", "Images", "Application", "coh-analytics-app-icon.png"));
        Assert.Equal(expected, embedded);
        Assert.DoesNotContain(">CA<", html);
    }

    [Fact]
    public void Rendering_same_projection_is_deterministic()
    {
        var segment = Segment(CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with { DamageDealt = Metric<CombatScaledAmount>.Available(new(12345)) }
            },
            Powers = [Outgoing("Deterministic pseudopower", 12345)]
        });
        var renderer = new HtmlReportRenderer();
        Assert.Equal(renderer.Render(segment), renderer.Render(segment));
    }

    [Fact]
    public void Filenames_are_safe_deterministic_and_collision_resistant()
    {
        var h = Segment().Header with { CharacterDisplayNameAtCapture = "../bad<>:\"/\\|?*\0name. ", AccountStableId = "secret-account" };
        var first = SegmentReportService.GetFileName(h);
        Assert.Equal(first, SegmentReportService.GetFileName(h));
        Assert.EndsWith(".html", first);
        Assert.Contains("2026-09-16", first);
        Assert.DoesNotContain("secret-account", first);
        Assert.DoesNotContain(first, c => c < 32 || "<>:\"/\\|?*".Contains(c));
        Assert.NotEqual(first, SegmentReportService.GetFileName(h with { SegmentId = h.SegmentId + "different" }));
        Assert.True(SegmentReportService.GetFileName(h with { CharacterDisplayNameAtCapture = new string('x', 1000) }).Length < 150);
    }

    [Fact]
    public void Repeated_generation_replaces_same_app_owned_file_and_uses_historical_reader()
    {
        using var fixture = new ReportFixture();
        var service = fixture.Service;
        Assert.Null(service.GenerateAndOpen(fixture.Reader.Segment.Header.GameplaySessionId, 0));
        var path = Assert.Single(fixture.Launcher.Paths);
        Assert.Equal(ApplicationDataPaths.GetReportsDirectory(fixture.Root), Path.GetDirectoryName(path));
        File.WriteAllText(path, "old report");
        Assert.Null(service.GenerateAndOpen(fixture.Reader.Segment.Header.GameplaySessionId, 0));
        Assert.Equal(path, fixture.Launcher.Paths[1]);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        Assert.StartsWith("<!DOCTYPE html>", File.ReadAllText(path));
        Assert.Equal(2, fixture.Reader.Loads);
        Assert.False(fixture.Reader.Options?.IncludeSpine ?? false);
        Assert.Equal(Path.Combine(ApplicationDataPaths.GetApplicationRoot(), "Reports"), ApplicationDataPaths.GetReportsDirectory());
    }

    [Theory]
    [InlineData("load")]
    [InlineData("missing")]
    [InlineData("directory")]
    [InlineData("write")]
    [InlineData("browser")]
    public void Failures_are_nonfatal_actionable_and_do_not_leak_exception_paths(string stage)
    {
        using var fixture = new ReportFixture();
        if (stage == "load") fixture.Reader.Throws = true;
        if (stage == "missing") fixture.Reader.Missing = true;
        if (stage == "directory") File.WriteAllText(Path.Combine(fixture.Root, "Reports"), "blocks directory");
        if (stage == "write") Directory.CreateDirectory(Path.Combine(fixture.Root, "Reports", SegmentReportService.GetFileName(fixture.Reader.Segment.Header)));
        if (stage == "browser") fixture.Launcher.Throws = true;
        var message = fixture.Service.GenerateAndOpen(fixture.Reader.Segment.Header.GameplaySessionId, 0);
        Assert.NotNull(message);
        Assert.DoesNotContain(fixture.Root, message);
        Assert.DoesNotContain("sensitive-path", message);
        if (stage == "browser") Assert.Contains("report was saved", message);
        else Assert.Empty(fixture.Launcher.Paths);
    }

    [Fact]
    public void Windows_launch_prepares_shell_execution_without_browser_arguments()
    {
        var path = Path.Combine(Path.GetTempPath(), "Report with spaces.html");
        var command = WindowsReportBrowserLauncher.Prepare(path);
        Assert.Equal(path, command.FileName);
        Assert.True(command.UseShellExecute);
        Assert.Equal("", command.Arguments);
    }

    [Fact]
    public void Renderer_has_no_engine_parser_dedup_or_lookup_dependencies()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoHAnalytics", "Services", "HtmlReportRenderer.cs"));
        foreach (var forbidden in new[] { "CombatEngine", "LogParser", "DedupEngine", "IItemReferenceCatalog", "ICharacterBuildSnapshotStore", "FrozenBuildManifestFactory", "Process.Start", "File.Read" })
            Assert.DoesNotContain(forbidden, source);
        Assert.Empty(typeof(HtmlReportRenderer).GetConstructors().Single().GetParameters());
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "CoHAnalytics.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return root.FullName;
    }

    private static CombatPowerAnalysisRow Outgoing(string name, long damageHundredths) => new()
    {
        Scope = CombatAnalyticsScope.Self,
        Direction = CombatAnalyticsDirection.Outgoing,
        PowerName = name,
        DamageMagnitude = new(damageHundredths),
        DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(damageHundredths))
    };

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

    private static HistoricalSegment Segment(CombatAnalyticsProjection? projection = null) => new()
    {
        Header = new()
        {
            SegmentId = "segment-identity", GameplaySessionId = GameplaySessionId.FromGuid(Guid.Empty), SegmentOrdinal = 0,
            CaptureKind = HistoricalCaptureKind.DurableSegment, Compatibility = HistoricalCompatibility.AuthoritativeAggregate,
            CaptureStartUtc = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), CaptureEndUtc = new(2026, 9, 16, 1, 0, 0, TimeSpan.Zero),
            CharacterDisplayNameAtCapture = "Test Hero"
        },
        Aggregates = projection ?? CombatAnalyticsProjection.Empty,
        DetailStatus = HistoricalDetailStatus.NotRequested, BuildContextStatus = HistoricalBuildContextStatus.NotCaptured,
        AnnotationStatus = HistoricalAnnotationStatus.Missing
    };

    private sealed class ReportFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "CoHAnalytics-ReportTests", Guid.NewGuid().ToString("N"));
        public Reader Reader { get; } = new();
        public Launcher Launcher { get; } = new();
        public SegmentReportService Service => new(Reader, Launcher, Root);
        public ReportFixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class Launcher : IReportBrowserLauncher
    {
        public List<string> Paths { get; } = [];
        public bool Throws { get; set; }
        public void Open(string path)
        {
            Paths.Add(path);
            if (Throws) throw new InvalidOperationException("sensitive-path");
        }
    }

    private sealed class Reader : IHistoricalSegmentReader
    {
        public HistoricalSegment Segment { get; } = SegmentReportTests.Segment();
        public bool Throws { get; set; }
        public bool Missing { get; set; }
        public int Loads { get; private set; }
        public HistoricalLoadOptions? Options { get; private set; }
        public IReadOnlyList<HistoricalSegmentHeader> ListHeaders(HistoricalSegmentQuery? query = null) => throw new NotSupportedException();
        public HistoricalLoadResult TryLoad(string segmentId, HistoricalLoadOptions? options = null) => throw new NotSupportedException();
        public HistoricalLoadResult TryLoad(GameplaySessionId gameplaySessionId, int segmentOrdinal, HistoricalLoadOptions? options = null)
        {
            Loads++;
            Options = options;
            Assert.Equal(Segment.Header.GameplaySessionId, gameplaySessionId);
            Assert.Equal(Segment.Header.SegmentOrdinal, segmentOrdinal);
            if (Throws) throw new IOException("sensitive-path");
            return new() { Outcome = Missing ? HistoricalLoadOutcome.NotFound : HistoricalLoadOutcome.Loaded, Segment = Missing ? null : Segment };
        }
    }
}
