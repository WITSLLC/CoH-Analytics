using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Presentation only: consumes the historical DTO and its authoritative cube.</summary>
public sealed class HtmlReportRenderer
{
    private const string ReportLogoResourceName = "CoHAnalytics.ReportAppLogo.png";
    private static readonly Lazy<string> ReportLogoDataUri = new(CreateReportLogoDataUri);

    public string Render(HistoricalSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var projection = segment.Aggregates
            ?? throw new ArgumentException("An authoritative projection is required.", nameof(segment));
        var b = new StringBuilder(32_768);
        b.Append(DocumentStart);
        Header(b, segment, projection);
        b.Append("<main>");
        Summary(b, projection);
        Offense(b, projection);
        DamageTypes(b, projection.DamageTypeBreakdown, "Damage type details", "outgoing-damage-types");
        Survivability(b, projection);
        Pets(b, projection);
        Procs(b, projection.Attribution);
        Support(b, projection);
        Targets(b, projection.Targets);
        Activity(b, projection);
        Coverage(b, segment, projection);
        b.Append("</main><footer>Historical Segment · authoritative stored projection · capture-time semantics preserved</footer>")
            .Append(DocumentScript).Append("</body></html>");
        return b.ToString();
    }

    private static void Header(StringBuilder b, HistoricalSegment segment, CombatAnalyticsProjection projection)
    {
        var h = segment.Header;
        b.Append("<header class='topbar'><div class='brand'><img src='").Append(ReportLogoDataUri.Value)
            .Append("' alt='CoH Analytics application logo'><div><strong>CoH Analytics</strong><span>Detailed Combat Report</span></div></div>")
            .Append("<div class='report-kind'><span>ANALYSIS</span><small>Historical Segment</small></div></header>")
            .Append("<div class='breadcrumb'>Analysis <b>/</b> Historical Segment <b>/</b> Detailed</div>")
            .Append("<section class='hero panel'><div><p class='eyebrow'>Selected session</p><h1>")
            .Append(E(h.CharacterDisplayNameAtCapture ?? "Unknown character")).Append("</h1>");

        var identity = new List<string>();
        if (!string.IsNullOrWhiteSpace(h.Archetype)) identity.Add(h.Archetype);
        if (!string.IsNullOrWhiteSpace(h.PrimaryPowerSet)) identity.Add(h.PrimaryPowerSet);
        if (!string.IsNullOrWhiteSpace(h.SecondaryPowerSet)) identity.Add(h.SecondaryPowerSet);
        if (h.LevelAtCapture is { } level) identity.Add($"Level {level}");
        if (identity.Count > 0) b.Append("<p class='identity'>").Append(E(string.Join(" · ", identity))).Append("</p>");
        if (!string.IsNullOrWhiteSpace(segment.Annotations?.UserDisplayName))
            b.Append("<p class='session-label'>").Append(E(segment.Annotations.UserDisplayName)).Append("</p>");

        b.Append("</div><dl class='context-grid'>");
        Context(b, "Capture start", Utc(h.CaptureStartUtc));
        Context(b, "Capture end", Utc(h.CaptureEndUtc));
        if (Show(projection.Clock.WallClockDuration))
            Context(b, "Capture wall", Duration(projection.Clock.WallClockDuration.Value!.Value));
        if (Show(projection.Clock.TrackedPauseAdjustedDuration))
            Context(b, "Tracked duration", Duration(projection.Clock.TrackedPauseAdjustedDuration.Value!.Value));
        if (segment.BuildContextStatus == HistoricalBuildContextStatus.Present)
        {
            Context(b, "Build context", "Frozen build context attached");
            var hash = projection.BuildContext.ManifestHash ?? h.BuildManifestHash;
            if (!string.IsNullOrWhiteSpace(hash)) Context(b, "Manifest", Abbreviate(hash));
        }
        b.Append("</dl></section>");
    }

    private static void Summary(StringBuilder b, CombatAnalyticsProjection p)
    {
        var cards = new List<CardData>();
        Add(cards, "Total Damage", p.Session.Metrics.DamageDealt, Amount);
        Add(cards, "Player Damage", p.Session.Metrics.DamageDealtSelf, Amount);
        Add(cards, "Pet Damage", p.Session.Metrics.DamageDealtOwnedPets, Amount);
        Add(cards, "Proc Damage", p.Attribution.ProcDamage, Amount, showIncompleteNote: false);
        Add(cards, "Proc Contribution", p.Attribution.ProcContributionHundredths, v => DecimalHundredths(v) + "%", showIncompleteNote: false);
        Add(cards, "Session DPS", p.Clock.WallClockDamagePerSecondHundredths, DecimalHundredths, "Based on total elapsed session time.");
        Add(cards, "Damage Taken", p.Session.Metrics.DamageReceived, Amount);
        Add(cards, "Healing Dealt", p.Session.Metrics.HealingDealt, Amount);
        Add(cards, "Healing Received", p.Session.Metrics.HealingReceived, Amount);
        Add(cards, "Endurance Granted", p.Session.Metrics.EnduranceGranted, Amount);
        Add(cards, "Endurance Received", p.Session.Metrics.EnduranceReceived, Amount);
        if (cards.Count == 0) return;
        SectionStart(b, "Combat summary", "Authoritative session totals", "summary");
        b.Append("<div class='metric-grid'>");
        foreach (var card in cards) Card(b, card);
        b.Append("</div></section>");
    }

    private static void Offense(StringBuilder b, CombatAnalyticsProjection p)
    {
        var rows = p.Powers.Where(r => r.Direction == CombatAnalyticsDirection.Outgoing
                && r.Scope != CombatAnalyticsScope.PerPet && Show(r.DamageMagnitudeMetric))
            .OrderByDescending(r => r.DamageMagnitudeMetric.Value!.Value.Hundredths).ThenBy(r => r.PowerName, StringComparer.Ordinal)
            .ToList();
        if (rows.Count == 0) return;
        SectionStart(b, "Where did the damage come from?", "Outgoing damage sources ranked by observed damage", "offense");
        var chartData = rows.Select(r => new DamageChartSource(
            r.PowerName, SourceLabel(r), r.DamageMagnitudeMetric.Value!.Value.Hundredths,
            Amount(r.DamageMagnitudeMetric.Value.Value))).ToList();
        var chartHeights = DamageChartHeights(chartData.Count);
        var total = Show(p.Session.Metrics.DamageDealt) ? Amount(p.Session.Metrics.DamageDealt.Value!.Value) : null;
        b.Append("<div class='chart-toolbar' role='group' aria-label='Damage chart view'>")
            .Append("<button type='button' data-chart-mode='donut' aria-pressed='false'>Donut</button>")
            .Append("<button type='button' data-chart-mode='pie' aria-pressed='false'>Pie</button>")
            .Append("<button type='button' class='active' data-chart-mode='bar' aria-pressed='true'>Bar</button></div>")
            .Append("<div class='damage-chart' data-damage-chart data-chart-source='outgoing-damage-data' data-total='").Append(E(total)).Append("'>")
            .Append("<div class='chart-viewport' id='damage-chart-viewport' style='--chart-height:")
            .Append(chartHeights.Desktop.ToString(CultureInfo.InvariantCulture)).Append("px;--chart-mobile-height:")
            .Append(chartHeights.Mobile.ToString(CultureInfo.InvariantCulture)).Append("px'></div></div>")
            .Append("<script type='application/json' id='outgoing-damage-data'>")
            .Append(JsonSerializer.Serialize(chartData)).Append("</script>");
        var columns = new List<Column<CombatPowerAnalysisRow>>
        {
            new("Damage Source", r => r.PowerName), new("Scope", SourceLabel),
            new("Damage", r => Amount(r.DamageMagnitudeMetric.Value!.Value), "number")
        };
        if (rows.Any(r => r.DirectAmount.Hundredths != 0)) columns.Add(new("Direct", r => Amount(r.DirectAmount), "number"));
        if (rows.Any(r => r.DotAmount.Hundredths != 0)) columns.Add(new("DoT", r => Amount(r.DotAmount), "number"));
        if (rows.Any(r => r.ActivationCount != 0)) columns.Add(new("Observed Activations", r => Count(r.ActivationCount), "number"));
        if (rows.Any(r => r.EventCount != 0)) columns.Add(new("Events", r => Count(r.EventCount), "number"));
        if (rows.Any(r => r.LargestHit.HasValue)) columns.Add(new("Max Hit", r => r.LargestHit is { } hit ? Amount(hit) : null, "number"));
        if (rows.Any(r => r.CoverageLimited || r.IsOverflow)) columns.Add(new("Coverage", RowCaveat));
        Table(b, rows, columns);
        b.Append("</section>");
    }

    private static void DamageTypes(StringBuilder b, MetricRef<IReadOnlyList<CombatDamageTypeTotal>> metric, string title, string id)
    {
        if (!Show(metric) || metric.Value!.Count == 0) return;
        var rows = metric.Value.OrderByDescending(r => r.Amount.Hundredths).ThenBy(r => r.DamageType).ToList();
        SectionStart(b, title, "Observed amounts; no contribution shares are inferred", id);
        if (metric.Availability == MetricAvailability.Incomplete) Caveat(b, PlayerCoverageText(metric.Coverage) ?? "Partial — some activity may not have been captured.");
        var columns = new List<Column<CombatDamageTypeTotal>>
        {
            new("Damage Type", r => DamageTypeLabel(r.DamageType)), new("Amount", r => Amount(r.Amount), "number")
        };
        if (rows.Any(r => r.EventCount != 0)) columns.Add(new("Events", r => Count(r.EventCount), "number"));
        if (rows.Any(r => r.IsOverflow)) columns.Add(new("Coverage", r => r.IsOverflow ? "Additional damage types grouped" : null));
        Table(b, rows, columns);
        b.Append("</section>");
    }

    private static void Survivability(StringBuilder b, CombatAnalyticsProjection p)
    {
        var incoming = p.Powers.Where(r => r.Direction == CombatAnalyticsDirection.Incoming
                && r.Scope != CombatAnalyticsScope.PerPet && Show(r.DamageMagnitudeMetric))
            .OrderByDescending(r => r.DamageMagnitudeMetric.Value!.Value.Hundredths).ThenBy(r => r.PowerName, StringComparer.Ordinal).ToList();
        var hasTotals = Show(p.Session.Metrics.DamageReceived) || Show(p.Session.Metrics.DamageReceivedOwnedPets);
        var hasTypes = Show(p.IncomingDamageTypeBreakdown) && p.IncomingDamageTypeBreakdown.Value!.Count > 0;
        if (!hasTotals && incoming.Count == 0 && !hasTypes) return;
        SectionStart(b, "Survivability", "Observed incoming damage", "survivability");
        var cards = new List<CardData>();
        Add(cards, "Total Damage Taken", p.Session.Metrics.DamageReceived, Amount);
        Add(cards, "Pet Damage Taken", p.Session.Metrics.DamageReceivedOwnedPets, Amount);
        if (cards.Count > 0) { b.Append("<div class='metric-grid compact'>"); foreach (var card in cards) Card(b, card); b.Append("</div>"); }
        if (incoming.Count > 0)
        {
            b.Append("<h3>Top incoming powers</h3>");
            var columns = new List<Column<CombatPowerAnalysisRow>> { new("Power", r => r.PowerName), new("Scope", SourceLabel),
                new("Damage", r => Amount(r.DamageMagnitudeMetric.Value!.Value), "number") };
            if (incoming.Any(r => r.EventCount != 0)) columns.Add(new("Events", r => Count(r.EventCount), "number"));
            if (incoming.Any(r => r.LargestHit.HasValue)) columns.Add(new("Largest Observed Hit", r => r.LargestHit is { } hit ? Amount(hit) : null, "number"));
            Table(b, incoming, columns);
        }
        b.Append("</section>");
        if (hasTypes) DamageTypes(b, p.IncomingDamageTypeBreakdown, "Incoming damage by type", "incoming-damage-types");
    }

    private static void Pets(StringBuilder b, CombatAnalyticsProjection p)
    {
        var rows = p.Actors.Where(r => r.Scope == CombatAnalyticsScope.PerPet && HasActorValue(r))
            .OrderByDescending(r => r.DamageDealt.Hundredths).ThenBy(r => r.PetDisplayName ?? r.PetNormalizedName, StringComparer.Ordinal).ToList();
        if (rows.Count == 0) return;
        SectionStart(b, "Pet contribution", "Owned pets are rolled up by normalized name", "pets");
        if (rows.Any(r => r.CoverageLimited)) Caveat(b, "Same-name pet instances are combined; exact instance splits are not captured.");
        var columns = new List<Column<CombatActorSummary>> { new("Pet", r => r.PetDisplayName ?? r.PetNormalizedName ?? "Owned pet") };
        Optional(columns, rows, "Damage", r => r.DamageDealt.Hundredths, r => Amount(r.DamageDealt));
        Optional(columns, rows, "Activations", r => r.ActivationCount, r => Count(r.ActivationCount));
        Optional(columns, rows, "Accuracy Attempts", r => r.Accuracy.Attempts, r => Count(r.Accuracy.Attempts));
        Optional(columns, rows, "Hits", r => r.Accuracy.Hits, r => Count(r.Accuracy.Hits));
        Optional(columns, rows, "Misses", r => r.Accuracy.Misses, r => Count(r.Accuracy.Misses));
        Optional(columns, rows, "Healing Dealt", r => r.HealingDealt.Hundredths, r => Amount(r.HealingDealt));
        Optional(columns, rows, "Endurance Granted", r => r.EnduranceGranted.Hundredths, r => Amount(r.EnduranceGranted));
        Table(b, rows, columns);
        b.Append("</section>");
    }

    private static void Procs(StringBuilder b, CombatProcAttributionSummary a)
    {
        var rows = a.ByParent.Where(r => Show(r.ProcDamageMetric))
            .OrderByDescending(r => r.ProcDamageMetric.Value!.Value.Hundredths).ThenBy(r => r.ExactProcIdentity, StringComparer.Ordinal).ToList();
        if (!Show(a.ProcDamage) && !Show(a.ProcContributionHundredths) && rows.Count == 0) return;
        SectionStart(b, "Proc contribution", "Proc identity and parent-power details", "procs");
        var cards = new List<CardData>();
        Add(cards, "Proc Damage", a.ProcDamage, Amount, showIncompleteNote: false);
        Add(cards, "Proc Contribution", a.ProcContributionHundredths, v => DecimalHundredths(v) + "%", showIncompleteNote: false);
        if (cards.Count > 0) { b.Append("<div class='metric-grid compact'>"); foreach (var card in cards) Card(b, card); b.Append("</div>"); }
        var procCaveat = ProcCaveat(a);
        if (procCaveat is not null) Caveat(b, procCaveat);
        if (rows.Count > 0)
        {
            var columns = new List<Column<CombatProcParentRow>> { new("Proc", r => r.ExactProcIdentity ?? "Unknown proc source") };
            if (rows.Any(r => r.Mode == ProcAttributionMode.BuildConfirmed && !string.IsNullOrWhiteSpace(r.ParentPowerName ?? r.ParentPowerId)))
                columns.Add(new("Parent Power", r => r.Mode == ProcAttributionMode.BuildConfirmed ? r.ParentPowerName ?? r.ParentPowerId : null));
            columns.Add(new("Attribution", r => AttributionLabel(r.Mode)));
            columns.Add(new("Damage", r => Amount(r.ProcDamageMetric.Value!.Value), "number"));
            if (rows.Any(r => r.EventCount != 0)) columns.Add(new("Events", r => Count(r.EventCount), "number"));
            if (rows.Any(r => r.Confidence.HasValue)) columns.Add(new("Confidence", r => ConfidenceLabel(r.Confidence)));
            Table(b, rows, columns);
        }
        b.Append("</section>");
    }

    private static void Support(StringBuilder b, CombatAnalyticsProjection p)
    {
        var powers = p.Powers.Where(r => r.Direction == CombatAnalyticsDirection.Outgoing && r.Scope != CombatAnalyticsScope.PerPet
                && (Show(r.HealingMagnitudeMetric) || Show(r.EnduranceMagnitudeMetric)))
            .OrderByDescending(r => Math.Max(r.HealingMagnitude.Hundredths, r.EnduranceMagnitude.Hundredths)).ThenBy(r => r.PowerName, StringComparer.Ordinal).ToList();
        var cards = new List<CardData>();
        Add(cards, "Healing Dealt", p.Session.Metrics.HealingDealt, Amount);
        Add(cards, "Healing Received", p.Session.Metrics.HealingReceived, Amount);
        Add(cards, "Endurance Granted", p.Session.Metrics.EnduranceGranted, Amount);
        Add(cards, "Endurance Received", p.Session.Metrics.EnduranceReceived, Amount);
        var actors = p.Actors.Where(r => r.HealingDealt.Hundredths != 0 || r.HealingReceived.Hundredths != 0
                || r.EnduranceGranted.Hundredths != 0 || r.EnduranceReceived.Hundredths != 0)
            .OrderBy(r => r.Scope).ThenBy(r => r.PetDisplayName ?? r.PetNormalizedName, StringComparer.Ordinal).ToList();
        if (cards.Count == 0 && powers.Count == 0 && actors.Count == 0) return;
        SectionStart(b, "Healing & support", "Directional support totals remain separate", "support");
        if (cards.Count > 0) { b.Append("<div class='metric-grid compact'>"); foreach (var card in cards) Card(b, card); b.Append("</div>"); }
        if (powers.Count > 0)
        {
            var columns = new List<Column<CombatPowerAnalysisRow>> { new("Power", r => r.PowerName), new("Source", SourceLabel) };
            if (powers.Any(r => Show(r.HealingMagnitudeMetric))) columns.Add(new("Healing", r => Show(r.HealingMagnitudeMetric) ? Amount(r.HealingMagnitudeMetric.Value!.Value) : null, "number"));
            if (powers.Any(r => Show(r.EnduranceMagnitudeMetric))) columns.Add(new("Endurance", r => Show(r.EnduranceMagnitudeMetric) ? Amount(r.EnduranceMagnitudeMetric.Value!.Value) : null, "number"));
            if (powers.Any(r => r.EventCount != 0)) columns.Add(new("Events", r => Count(r.EventCount), "number"));
            Table(b, powers, columns);
        }
        if (actors.Count > 0)
        {
            b.Append("<h3>Support by actor</h3>");
            var columns = new List<Column<CombatActorSummary>> { new("Source", ActorLabel) };
            Optional(columns, actors, "Healing Dealt", r => r.HealingDealt.Hundredths, r => Amount(r.HealingDealt));
            Optional(columns, actors, "Healing Received", r => r.HealingReceived.Hundredths, r => Amount(r.HealingReceived));
            Optional(columns, actors, "Endurance Granted", r => r.EnduranceGranted.Hundredths, r => Amount(r.EnduranceGranted));
            Optional(columns, actors, "Endurance Received", r => r.EnduranceReceived.Hundredths, r => Amount(r.EnduranceReceived));
            Table(b, actors, columns);
        }
        b.Append("</section>");
    }

    private static void Targets(StringBuilder b, IReadOnlyList<CombatTargetSummary> source)
    {
        var rows = source.Where(r => r.DamageDealt.Hundredths != 0 || r.EventCount != 0)
            .OrderByDescending(r => r.DamageDealt.Hundredths).ThenBy(r => r.DisplayName ?? r.NormalizedTargetName, StringComparer.Ordinal).ToList();
        if (rows.Count == 0) return;
        SectionStart(b, "Targets", "Observed target damage totals", "targets");
        if (rows.Any(r => r.IsOverflow)) Caveat(b, "Additional targets are grouped in the persisted overflow row.");
        var columns = new List<Column<CombatTargetSummary>> { new("Target", r => r.DisplayName ?? r.NormalizedTargetName),
            new("Damage", r => Amount(r.DamageDealt), "number") };
        if (rows.Any(r => r.EventCount != 0)) columns.Add(new("Events", r => Count(r.EventCount), "number"));
        Table(b, rows, columns);
        b.Append("</section>");
    }

    private static void Activity(StringBuilder b, CombatAnalyticsProjection p)
    {
        var cards = new List<CardData>();
        Add(cards, "Observed Activations", p.Session.Metrics.ActivationCount, Count);
        Add(cards, "Recharge Completed", p.Session.Metrics.ConfirmedRechargeCompletedCount, Count);
        Add(cards, "Still Recharging", p.Session.Metrics.ConfirmedStillRechargingCount, Count);
        if (Show(p.Session.Metrics.Accuracy))
        {
            var accuracy = p.Session.Metrics.Accuracy;
            cards.Add(new("Accuracy Resolutions", $"{Count(accuracy.Value!.Hits)} hits · {Count(accuracy.Value.Misses)} misses · {Count(accuracy.Value.Attempts)} attempts",
                accuracy.Availability == MetricAvailability.Incomplete
                    ? PlayerCoverageText(accuracy.Coverage) ?? "Partial — some activity may not have been captured."
                    : null));
        }
        var powers = p.Powers.Where(r => r.ActivationCount != 0 || r.ConfirmedRechargeCompletedCount != 0 || r.ConfirmedStillRechargingCount != 0)
            .OrderByDescending(r => r.ActivationCount).ThenBy(r => r.PowerName, StringComparer.Ordinal).ToList();
        if (cards.Count == 0 && powers.Count == 0) return;
        SectionStart(b, "Observed power activity", "Captured observations; no theoretical recharge is inferred", "activity");
        if (cards.Count > 0) { b.Append("<div class='metric-grid compact'>"); foreach (var card in cards) Card(b, card); b.Append("</div>"); }
        if (powers.Count > 0)
        {
            var columns = new List<Column<CombatPowerAnalysisRow>> { new("Power", r => r.PowerName), new("Source", SourceLabel) };
            if (powers.Any(r => r.ActivationCount != 0)) columns.Add(new("Observed Activations", r => Count(r.ActivationCount), "number"));
            if (powers.Any(r => r.ConfirmedRechargeCompletedCount != 0)) columns.Add(new("Recharge Completed", r => Count(r.ConfirmedRechargeCompletedCount), "number"));
            if (powers.Any(r => r.ConfirmedStillRechargingCount != 0)) columns.Add(new("Still Recharging", r => Count(r.ConfirmedStillRechargingCount), "number"));
            Table(b, powers, columns);
        }
        b.Append("</section>");
    }

    private static void Coverage(StringBuilder b, HistoricalSegment segment, CombatAnalyticsProjection p)
    {
        var caveats = PlayerCaveats(segment, p).Distinct(StringComparer.Ordinal).ToList();
        b.Append("<details class='verification'><summary><span>Coverage &amp; verification</span><small>Player caveats and deep diagnostics</small></summary>");
        if (caveats.Count > 0)
        {
            b.Append("<div class='caveat-panel'><h3>Player-facing caveats</h3><ul>");
            foreach (var caveat in caveats) b.Append("<li>").Append(E(caveat)).Append("</li>");
            b.Append("</ul></div>");
        }
        b.Append("<h3>Deep diagnostics</h3><dl class='diagnostics'>");
        Diagnostic(b, "Analytics semantic version", p.AnalyticsSemanticVersion);
        Diagnostic(b, "Segment schema version", segment.Header.SegmentSchemaVersion);
        Diagnostic(b, "Spine schema version", segment.Header.SpineSchemaVersion);
        Diagnostic(b, "Attribution policy", segment.Header.AttributionPolicyVersion ?? p.Attribution.AttributionPolicyVersion);
        Diagnostic(b, "Manifest hash", p.BuildContext.ManifestHash ?? segment.Header.BuildManifestHash);
        Diagnostic(b, "Catalog fingerprint", p.BuildContext.BuildCatalogFingerprint ?? segment.Header.BuildCatalogFingerprint);
        Diagnostic(b, "Logical events", segment.Coverage?.LogicalEventCount ?? p.LogicalEventsApplied);
        Diagnostic(b, "Duplicate occurrences ignored", segment.Coverage?.DuplicateOccurrencesIgnored ?? p.DuplicateOccurrencesIgnored);
        Diagnostic(b, "Retained spine events", segment.Coverage?.RetainedSpineEventCount);
        Diagnostic(b, "Spine retention limit", segment.Coverage?.SpineRetentionLimit);
        Diagnostic(b, "Spine truncated", segment.Coverage is null ? null : segment.Coverage.SpineTruncated ? "Yes" : "No");
        b.Append("</dl>");
        if (segment.Coverage is { } coverage)
        {
            var replay = ReplayRows(coverage.Replay);
            Table(b, replay, [new("Dimension", r => r.Label), new("Persisted Aggregate", r => r.Entry.AggregateAuthoritative ? "Authoritative" : "Not available"),
                new("Retained Spine", r => ReplayLabel(r.Entry.Replay))]);
        }
        b.Append("</details>");
    }

    private static IReadOnlyList<string> PlayerCaveats(HistoricalSegment segment, CombatAnalyticsProjection p)
    {
        var result = new List<string>();
        if (p.CoverageLimited || segment.Coverage?.CoverageLimited == true) result.Add("Some activity may not have been fully captured.");
        if (p.Actors.Any(r => r.Scope == CombatAnalyticsScope.PerPet && r.CoverageLimited)) result.Add("Owned pets with the same name are combined.");
        if (p.DamageTypeBreakdown.Coverage is { MissingDamageType: true } || p.IncomingDamageTypeBreakdown.Coverage is { MissingDamageType: true }) result.Add("Some events do not include a captured damage type.");
        if (p.Targets.Any(r => r.IsOverflow) || p.Session.Metrics.DistinctTargetCount.Coverage is { MissingTarget: true }) result.Add("Target detail includes an overflow or missing-target limitation.");
        if (p.Attribution.ParentRowsIncomplete) result.Add("Some proc-parent attribution rows are incomplete.");
        if (segment.BuildContextStatus != HistoricalBuildContextStatus.Present) result.Add("No usable frozen build context is attached to this Segment.");
        return result;
    }

    private static IReadOnlyList<ReplayRow> ReplayRows(LosslessReplayCoverageMatrix r) =>
    [
        new("Session totals", r.SessionTotals), new("Power totals", r.PerPowerTotals), new("Damage types", r.DamageType),
        new("Direct / DoT", r.DirectVersusDot), new("Actors / pets", r.ActorPet), new("Targets", r.Target),
        new("Accuracy", r.Accuracy), new("Activations", r.Activation), new("Recharge observations", r.LifecycleRecharge),
        new("Clock", r.Clock), new("Frozen build context", r.FrozenBuildContext), new("Proc attribution", r.ProcAttribution),
        new("Event timeline", r.EventTimeline)
    ];

    private static void SectionStart(StringBuilder b, string title, string subtitle, string id) =>
        b.Append("<section class='report-section' data-section='").Append(E(id)).Append("'><div class='section-heading'><div><p class='eyebrow'>Detailed analysis</p><h2>")
            .Append(E(title)).Append("</h2></div><p>").Append(E(subtitle)).Append("</p></div>");

    private static void Card(StringBuilder b, CardData card)
    {
        b.Append("<article class='metric-card'><span>").Append(E(card.Label)).Append("</span><strong>").Append(E(card.Value)).Append("</strong>");
        if (!string.IsNullOrWhiteSpace(card.Note)) b.Append("<small class='metric-note'>").Append(E(card.Note)).Append("</small>");
        b.Append("</article>");
    }

    private static void Add<T>(List<CardData> cards, string label, Metric<T> metric, Func<T, string> format,
        string? note = null, bool showIncompleteNote = true) where T : struct
    {
        if (!Show(metric)) return;
        var caveat = metric.Availability == MetricAvailability.Incomplete && showIncompleteNote
            ? PlayerCoverageText(metric.Coverage) ?? "Partial — some activity may not have been captured."
            : note;
        cards.Add(new(label, format(metric.Value!.Value), caveat));
    }

    private static void Table<T>(StringBuilder b, IReadOnlyList<T> rows, IReadOnlyList<Column<T>> columns)
    {
        if (rows.Count == 0 || columns.Count == 0) return;
        b.Append("<div class='table-wrap'><table><thead><tr>");
        foreach (var column in columns) b.Append("<th scope='col' class='").Append(E(column.CssClass)).Append("'>").Append(E(column.Label)).Append("</th>");
        b.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            b.Append("<tr>");
            foreach (var column in columns)
            {
                var value = column.Value(row);
                b.Append("<td class='").Append(E(column.CssClass)).Append("'>").Append(value is null ? "<span class='empty'>—</span>" : E(value)).Append("</td>");
            }
            b.Append("</tr>");
        }
        b.Append("</tbody></table></div>");
    }

    private static void Optional<T>(List<Column<T>> columns, IReadOnlyList<T> rows, string label, Func<T, long> raw, Func<T, string> value)
    {
        if (rows.Any(r => raw(r) != 0)) columns.Add(new(label, value, "number"));
    }

    private static void Context(StringBuilder b, string label, string value) =>
        b.Append("<div><dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd></div>");

    private static void Diagnostic(StringBuilder b, string label, object? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture))) return;
        b.Append("<div><dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd></div>");
    }

    private static void Caveat(StringBuilder b, string text) => b.Append("<p class='callout'>").Append(E(text)).Append("</p>");
    private static bool Show<T>(Metric<T> metric) where T : struct => metric.Value.HasValue && metric.Availability is MetricAvailability.Available or MetricAvailability.Incomplete;
    private static bool Show<T>(MetricRef<T> metric) where T : class => metric.Value is not null && metric.Availability is MetricAvailability.Available or MetricAvailability.Incomplete;
    private static string E(object? value) => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    private static string Amount(CombatScaledAmount value) => value.ToString();
    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string DecimalHundredths(long value) => (value / 100m).ToString("0.##", CultureInfo.InvariantCulture);
    private static (int Desktop, int Mobile) DamageChartHeights(int rowCount)
    {
        const int barRowHeight = 38;
        const int barRowGap = 8;
        const int radialPlotHeight = 300;
        const int legendRowHeight = 24;
        const int legendRowGap = 7;
        const int stackedLayoutGap = 20;
        var gaps = Math.Max(0, rowCount - 1);
        var barHeight = rowCount * barRowHeight + gaps * barRowGap;
        var legendHeight = rowCount * legendRowHeight + gaps * legendRowGap;
        var desktop = Math.Max(barHeight, Math.Max(radialPlotHeight, legendHeight));
        var mobile = Math.Max(barHeight, radialPlotHeight + stackedLayoutGap + legendHeight);
        return (desktop, mobile);
    }
    private static string Duration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    private static string Abbreviate(string value) => value.Length <= 12 ? value : value[..12] + "…";
    private static string DamageTypeLabel(DamageType value) => value.IsUnresistable ? "Unresistable " + value.Text : value.Text;
    private static string SourceLabel(CombatPowerAnalysisRow row) => row.Scope switch { CombatAnalyticsScope.Self => "Player", CombatAnalyticsScope.OwnPetsAggregate => "Owned pets", _ => row.PetDisplayName ?? row.PetNormalizedName ?? "Owned pet" };
    private static string ActorLabel(CombatActorSummary row) => row.Scope switch { CombatAnalyticsScope.Self => "Player", CombatAnalyticsScope.OwnPetsAggregate => "Owned pets", _ => row.PetDisplayName ?? row.PetNormalizedName ?? "Owned pet" };
    private static string? RowCaveat(CombatPowerAnalysisRow row) => row.IsOverflow ? "Additional sources grouped" : row.CoverageLimited ? "Partial — some activity may be missing" : null;
    private static bool HasActorValue(CombatActorSummary r) => r.DamageDealt.Hundredths != 0 || r.DamageReceived.Hundredths != 0 || r.HealingDealt.Hundredths != 0 || r.HealingReceived.Hundredths != 0 || r.EnduranceGranted.Hundredths != 0 || r.EnduranceReceived.Hundredths != 0 || r.ActivationCount != 0 || r.Accuracy.Attempts != 0;
    private static string AttributionLabel(ProcAttributionMode mode) => mode switch { ProcAttributionMode.BuildConfirmed => "Build confirmed", ProcAttributionMode.Unattributed => "Parent not attributed", ProcAttributionMode.Direct => "Direct", ProcAttributionMode.Correlated => "Correlated", _ => "Unspecified" };
    private static string? ConfidenceLabel(MetricConfidence? value) => value switch { MetricConfidence.High => "High", MetricConfidence.Medium => "Medium", MetricConfidence.Low => "Low", _ => null };
    private static string ReplayLabel(ReplayCoverageKind value) => value switch { ReplayCoverageKind.Lossless => "Lossless", ReplayCoverageKind.SufficientForRecompute => "Sufficient for recompute", ReplayCoverageKind.PartialReplay => "Partial replay", _ => "Not recomputable" };

    private static string? ProcCaveat(CombatProcAttributionSummary attribution)
    {
        var coverages = new[] { attribution.ProcDamage.Coverage, attribution.ProcContributionHundredths.Coverage };
        if (coverages.Any(c => c is { UnidentifiedProcSource: true }))
            return "Partial — some proc damage could not be identified by source.";
        if (attribution.ParentRowsIncomplete || coverages.Any(c => c is { AmbiguousProcParent: true }))
            return "Partial — some proc damage could not be linked to a parent power.";
        if (attribution.ProcDamage.Availability == MetricAvailability.Incomplete
            || attribution.ProcContributionHundredths.Availability == MetricAvailability.Incomplete)
            return "Partial — proc details are incomplete for this session.";
        return null;
    }

    private static string? PlayerCoverageText(CoverageInfo? value)
    {
        if (value is not { } c) return null;
        var labels = new List<string>();
        if (c.LowerBound) labels.Add("some activity may not have been captured");
        if (c.Overflow) labels.Add("additional entries are grouped");
        if (c.PetNameRollup) labels.Add("same-name pets are combined");
        if (c.MissingDamageType) labels.Add("some damage types are unknown");
        if (c.MissingTarget) labels.Add("some target details are missing");
        if (c.MissingBuildContext) labels.Add("frozen build details are unavailable");
        if (c.AmbiguousProcParent) labels.Add("some proc parent powers are uncertain");
        if (c.UnidentifiedProcSource) labels.Add("some proc damage sources could not be identified");
        return labels.Count == 0 ? null : "Partial — " + string.Join("; ", labels) + ".";
    }

    private static string CreateReportLogoDataUri()
    {
        using var stream = typeof(HtmlReportRenderer).Assembly.GetManifestResourceStream(ReportLogoResourceName)
            ?? throw new InvalidOperationException($"Embedded report logo '{ReportLogoResourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
    }

    private sealed record CardData(string Label, string Value, string? Note);
    private sealed record DamageChartSource(string Name, string Scope, long DamageHundredths, string DisplayDamage);
    private sealed record Column<T>(string Label, Func<T, string?> Value, string CssClass = "");
    private sealed record ReplayRow(string Label, ReplayCoverageEntry Entry);

    private const string DocumentScript = """
        <script>
        (()=>{
          const chart=document.querySelector('[data-damage-chart]');
          if(!chart)return;
          const host=document.getElementById('damage-chart-viewport');
          const source=document.getElementById(chart.dataset.chartSource);
          if(!host||!source)return;
          const data=JSON.parse(source.textContent);
          const colors=['#43d7e8','#249db2','#76a9c0','#8a7fd1','#d3ad67','#5ec59a','#d17386','#668bd4','#9cb65d','#b078c2','#d48b58','#4fb5a9'];
          const svgNs='http://www.w3.org/2000/svg';
          const make=(tag,attributes={})=>{const node=document.createElementNS(svgNs,tag);Object.entries(attributes).forEach(([key,value])=>node.setAttribute(key,value));return node};
          const point=(cx,cy,r,angle)=>[cx+r*Math.cos(angle),cy+r*Math.sin(angle)];
          const renderBar=target=>{
            const max=Math.max(1,...data.map(item=>item.DamageHundredths));
            data.forEach(item=>{
              const row=document.createElement('div');row.className='bar-row';
              const label=document.createElement('div');const name=document.createElement('strong');name.textContent=item.Name;
              const scope=document.createElement('span');scope.textContent=item.Scope;label.append(name,scope);
              const track=document.createElement('div');track.className='bar-track';const fill=document.createElement('div');fill.className='bar-fill';
              fill.style.width=`${item.DamageHundredths/max*100}%`;track.append(fill);
              const amount=document.createElement('b');amount.textContent=item.DisplayDamage;row.append(label,track,amount);target.append(row);
            });
          };
          const renderRadial=(target,mode)=>{
            const layout=document.createElement('div');layout.className='radial-layout';
            const svg=make('svg',{class:'radial-chart',role:'img','aria-label':`Outgoing damage ${mode} chart`,viewBox:'0 0 360 300'});
            const legend=document.createElement('div');legend.className='chart-legend';layout.append(svg,legend);target.append(layout);
            const visible=data.filter(item=>item.DamageHundredths>0);const total=visible.reduce((sum,item)=>sum+item.DamageHundredths,0);
            if(total<=0)return;
            let angle=-Math.PI/2;const cx=180,cy=150,outer=118,inner=mode==='donut'?68:0;
            visible.forEach((item,index)=>{
              const next=angle+(item.DamageHundredths/total)*Math.PI*2;const color=colors[index%colors.length];
              let shape;
              if(visible.length===1){shape=make('circle',{cx,cy,r:outer,fill:mode==='donut'?'none':color,stroke:color,'stroke-width':mode==='donut'?outer-inner:0});}
              else{
                const [sx,sy]=point(cx,cy,outer,angle),[ex,ey]=point(cx,cy,outer,next),large=next-angle>Math.PI?1:0;
                if(mode==='pie')shape=make('path',{d:`M ${cx} ${cy} L ${sx} ${sy} A ${outer} ${outer} 0 ${large} 1 ${ex} ${ey} Z`,fill:color});
                else{const [isx,isy]=point(cx,cy,inner,angle),[iex,iey]=point(cx,cy,inner,next);shape=make('path',{d:`M ${sx} ${sy} A ${outer} ${outer} 0 ${large} 1 ${ex} ${ey} L ${iex} ${iey} A ${inner} ${inner} 0 ${large} 0 ${isx} ${isy} Z`,fill:color});}
              }
              svg.append(shape);angle=next;
              const row=document.createElement('div');row.className='legend-row';const swatch=document.createElement('span');swatch.className='legend-swatch';swatch.style.backgroundColor=color;
              const name=document.createElement('span');name.textContent=item.Name;const amount=document.createElement('b');amount.textContent=item.DisplayDamage;row.append(swatch,name,amount);legend.append(row);
            });
            if(mode==='donut'&&chart.dataset.total){const label=make('text',{x:cx,y:cy-7,class:'chart-total-label'});label.textContent='Total damage';const value=make('text',{x:cx,y:cy+20,class:'chart-total-value'});value.textContent=chart.dataset.total;svg.append(label,value);}
          };
          const renderDamageChart=mode=>{
            host.replaceChildren();
            if(mode==='bar'){const bars=document.createElement('div');bars.className='bars';host.append(bars);renderBar(bars);}
            else renderRadial(host,mode);
          };
          const buttons=document.querySelectorAll('[data-chart-mode]');
          const setMode=mode=>{
            buttons.forEach(item=>{const active=item.dataset.chartMode===mode;item.classList.toggle('active',active);item.setAttribute('aria-pressed',active?'true':'false')});
            renderDamageChart(mode);
          };
          buttons.forEach(button=>button.addEventListener('click',()=>setMode(button.dataset.chartMode)));
          setMode('bar');
        })();
        </script>
        """;

    private const string DocumentStart = """
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
        <title>CoH Analytics — Detailed Combat Report</title><style>
        :root{color-scheme:dark;--bg:#061117;--surface:#0a1b22;--panel:#0d222b;--panel2:#102a34;--line:#1a4653;--line2:#246375;--text:#e9f6f8;--soft:#bed3d8;--muted:#789ba4;--cyan:#43d7e8;--cyan2:#249db2;--amber:#d3ad67}
        *{box-sizing:border-box}html{min-height:100%;background:radial-gradient(circle at 80% -10%,#123742 0,transparent 35%),linear-gradient(180deg,#07151c,#040b0f);color:var(--text);font-family:"Segoe UI",Inter,system-ui,sans-serif}body{max-width:1440px;margin:0 auto;padding:20px 30px 56px;line-height:1.45}
        .topbar{min-height:72px;display:flex;align-items:center;justify-content:space-between;border:1px solid var(--line);background:rgba(7,24,31,.95);padding:10px 18px;box-shadow:0 18px 50px rgba(0,0,0,.3)}.brand{display:flex;align-items:center;gap:13px}.brand img{display:block;width:48px;height:48px;object-fit:contain}.brand strong{display:block;font-size:17px;letter-spacing:.02em}.brand span{display:block;color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.13em;margin-top:2px}.report-kind{text-align:right}.report-kind span{display:block;color:var(--cyan);font-size:10px;font-weight:800;letter-spacing:.16em}.report-kind small{color:var(--soft)}
        .breadcrumb{padding:12px 4px;color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.1em}.breadcrumb b{padding:0 8px;color:#376470}.panel,.report-section,.verification{border:1px solid var(--line);background:linear-gradient(145deg,rgba(13,34,43,.98),rgba(8,25,32,.98));box-shadow:0 16px 42px rgba(0,0,0,.22)}.hero{display:flex;justify-content:space-between;gap:24px;padding:24px 26px}.hero h1{margin:1px 0 6px;font-size:29px;line-height:1.1}.identity,.session-label{margin:4px 0;color:var(--soft)}.session-label{color:var(--cyan)}.eyebrow{margin:0 0 6px;color:var(--cyan2);font-size:10px;font-weight:800;text-transform:uppercase;letter-spacing:.15em}.context-grid{display:grid;grid-template-columns:repeat(2,minmax(155px,1fr));gap:10px 26px;margin:0;min-width:min(540px,50%)}.context-grid div,.diagnostics div{border-left:2px solid #1c4b58;padding-left:10px}.context-grid dt,.diagnostics dt{color:var(--muted);font-size:9px;text-transform:uppercase;letter-spacing:.11em}.context-grid dd,.diagnostics dd{margin:2px 0 0;color:var(--soft);font-size:12px;overflow-wrap:anywhere}
        .report-section{margin-top:18px;padding:20px 22px}.section-heading{display:flex;align-items:flex-end;justify-content:space-between;gap:18px;margin-bottom:16px}.section-heading h2{margin:0;font-size:19px}.section-heading>p{margin:0;color:var(--muted);font-size:12px;text-align:right}.metric-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(175px,1fr));gap:10px}.metric-grid.compact{margin-bottom:16px}.metric-card{min-height:104px;padding:15px;border:1px solid #1b4855;background:linear-gradient(145deg,#102b34,#0b2028);position:relative;overflow:hidden}.metric-card:after{content:"";position:absolute;inset:auto 0 0;height:2px;background:linear-gradient(90deg,var(--cyan2),transparent)}.metric-card span{display:block;color:var(--muted);font-size:10px;text-transform:uppercase;letter-spacing:.1em}.metric-card strong{display:block;margin-top:8px;font-size:23px;font-weight:650;overflow-wrap:anywhere}.metric-card .metric-note{display:block;margin-top:7px;color:#8eb0b8;font-size:9px;letter-spacing:.02em}
        .chart-toolbar{display:flex;justify-content:flex-end;gap:4px;margin:-3px 0 12px}.chart-toolbar button{border:1px solid #22515e;background:#0a1c23;color:#7fa4ac;padding:6px 12px;font:600 10px "Segoe UI",sans-serif;text-transform:uppercase;letter-spacing:.08em;cursor:pointer}.chart-toolbar button:hover,.chart-toolbar button:focus-visible{border-color:var(--cyan2);color:var(--text)}.chart-toolbar button.active{border-color:var(--cyan);background:#103843;color:var(--cyan)}.damage-chart{margin-bottom:18px}.chart-viewport{height:var(--chart-height);overflow:auto}.bars{display:grid;gap:8px}.bar-row{display:grid;min-height:38px;grid-template-columns:minmax(160px,1fr) minmax(180px,3fr) 90px;align-items:center;gap:12px}.bar-row div strong,.bar-row div span{display:block}.bar-row div strong{font-size:12px}.bar-row div span{color:var(--muted);font-size:10px}.bar-row>b{text-align:right;color:var(--soft);font-size:12px}.bar-track{height:9px;border:1px solid #183f4a;background:#07151b}.bar-fill{height:100%;background:linear-gradient(90deg,#1e8295,var(--cyan))}.radial-layout{display:grid;height:100%;grid-template-columns:minmax(280px,1fr) minmax(230px,1fr);gap:28px;align-items:center}.radial-chart{display:block;width:100%;height:300px;margin:auto}.chart-legend{display:grid;gap:7px;align-content:center}.legend-row{display:grid;min-height:24px;grid-template-columns:9px minmax(120px,1fr) auto;align-items:center;gap:8px;color:var(--soft);font-size:11px}.legend-swatch{width:9px;height:9px}.legend-row b{font-variant-numeric:tabular-nums}.chart-total-label{fill:#759ba4;font-size:10px;text-anchor:middle;text-transform:uppercase;letter-spacing:.08em}.chart-total-value{fill:#e9f6f8;font-size:20px;font-weight:650;text-anchor:middle}
        h3{font-size:12px;text-transform:uppercase;letter-spacing:.09em;color:var(--soft);margin:20px 0 10px}.table-wrap{overflow:auto;border:1px solid #173e49;background:#081b22}table{width:100%;border-collapse:collapse}th{padding:9px 10px;color:#739ba4;font-size:9px;text-align:left;text-transform:uppercase;letter-spacing:.1em;border-bottom:1px solid var(--line2);white-space:nowrap}td{padding:10px;color:#c8dde1;font-size:11px;border-bottom:1px solid #143640;vertical-align:top}tbody tr:last-child td{border-bottom:0}tbody tr:hover{background:#0e2730}.number{text-align:right;font-variant-numeric:tabular-nums}.empty{color:#42616a}.callout{border-left:2px solid var(--amber);padding:8px 10px;background:#211e18;color:#d8c59d;font-size:11px}
        .verification{margin-top:18px;padding:0}.verification summary{display:flex;justify-content:space-between;align-items:center;cursor:pointer;padding:16px 20px;color:var(--soft);list-style-position:inside}.verification summary span{font-weight:650}.verification summary small{color:var(--muted)}.verification[open]{padding-bottom:20px}.verification[open] summary{border-bottom:1px solid var(--line)}.verification>h3,.verification>.table-wrap,.caveat-panel,.diagnostics{margin-left:20px;margin-right:20px}.caveat-panel{margin-top:18px;padding:12px 15px;border:1px solid #54472e;background:#1d1a14}.caveat-panel h3{margin:0 0 8px;color:#ddc38c}.caveat-panel ul{margin:0;padding-left:18px;color:#c9bb9e;font-size:11px}.diagnostics{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;margin-bottom:18px}.diagnostics div{min-height:42px}footer{margin-top:20px;padding-top:17px;border-top:1px solid #153640;color:var(--muted);font-size:10px;text-align:center;text-transform:uppercase;letter-spacing:.08em}
        @media(max-width:850px){body{padding:10px}.hero{display:block}.context-grid{min-width:0;margin-top:20px}.section-heading{display:block}.section-heading>p{text-align:left;margin-top:4px}.bar-row{grid-template-columns:1fr 2fr 70px}.chart-viewport{height:var(--chart-mobile-height)}.radial-layout{grid-template-columns:1fr;grid-template-rows:300px auto;gap:20px}}@media(max-width:560px){.context-grid{grid-template-columns:1fr}.metric-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.report-kind{display:none}.bar-row{grid-template-columns:1fr 90px}.bar-track{display:none}}@media print{body{max-width:none}.panel,.report-section,.verification{box-shadow:none}.verification{display:block}.topbar{box-shadow:none}.chart-toolbar{display:none}}
        </style></head><body>
        """;
}
