using System.Globalization;
using System.Net;
using System.Text;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Presentation only: consumes the historical DTO and its authoritative cube.</summary>
public sealed class HtmlReportRenderer
{
    public string Render(HistoricalSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var p = segment.Aggregates ?? throw new ArgumentException("An authoritative projection is required.", nameof(segment));
        var h = segment.Header;
        var b = new StringBuilder(DocumentStart);
        b.Append("<header class='topbar'><div class='brandmark'>CA</div><div><h1>CoH Analytics</h1><p class='muted'>Analytical Reference Output</p></div></header>")
            .Append("<section class='context card'><p class='eyebrow'>Selected historical Segment</p><h2>").Append(E(h.CharacterDisplayNameAtCapture ?? "Unknown character"))
            .Append("</h2><p>").Append(E(h.Archetype)).Append(" · ").Append(E(h.PrimaryPowerSet)).Append(" / ").Append(E(h.SecondaryPowerSet))
            .Append("</p><p>").Append(E(h.CaptureStartUtc.ToString("u"))).Append(" → ").Append(E(h.CaptureEndUtc.ToString("u")))
            .Append("</p><p>Segment ").Append(E(h.SegmentId)).Append(" · ").Append(E(h.CaptureKind)).Append(" · ").Append(E(h.Compatibility)).Append("</p></section>");
        b.Append("<main><h2>Session metrics</h2><div class='cards'>");
        Card(b, "Damage dealt", M(p.Session.Metrics.DamageDealt));
        Card(b, "Player damage", M(p.Session.Metrics.DamageDealtSelf));
        Card(b, "Owned pet damage", M(p.Session.Metrics.DamageDealtOwnedPets));
        Card(b, "Damage received", M(p.Session.Metrics.DamageReceived));
        Card(b, "Healing dealt", M(p.Session.Metrics.HealingDealt));
        Card(b, "Healing received", M(p.Session.Metrics.HealingReceived));
        Card(b, "Capture-wall DPS", M(p.Clock.WallClockDamagePerSecondHundredths, Hundredths));
        Card(b, "Capture-wall duration", M(p.Clock.WallClockDuration));
        Card(b, "Active combat duration", M(p.Clock.ActiveDuration));
        Card(b, "Active DPS", M(p.Clock.ActiveDamagePerSecondHundredths, Hundredths));
        Card(b, "Tracked pause-adjusted duration", M(p.Clock.TrackedPauseAdjustedDuration));
        Card(b, "Experience gained", M(segment.ExperienceGained));
        Card(b, "Gameplay influence gained", M(segment.GameplayInfluenceGained));
        Card(b, "Accuracy resolutions", Accuracy(p.Session.Metrics.Accuracy));
        b.Append("</div><p class='muted'>Rates use the supplied capture-wall projection. Capture timing is not an inferred gameplay interval.</p>");

        Table(b, "Powers", ["Power", "Direction", "Scope", "Pet", "Damage", "Healing", "Endurance", "Targets", "Overflow", "Coverage limited"]);
        foreach (var r in p.Powers)
            Row(b, r.PowerName, r.Direction, r.Scope, r.PetDisplayName ?? r.PetNormalizedName, M(r.DamageMagnitudeMetric), M(r.HealingMagnitudeMetric), M(r.EnduranceMagnitudeMetric), M(r.DistinctTargetCountMetric), r.IsOverflow, r.CoverageLimited);
        EndTable(b, p.Powers.Count);
        Types(b, "Outgoing damage types", p.DamageTypeBreakdown);
        Types(b, "Incoming damage types", p.IncomingDamageTypeBreakdown);
        Table(b, "Actors and pets", ["Scope", "Pet", "Damage dealt", "Damage received", "Pet instances", "Overflow", "Coverage limited"]);
        foreach (var r in p.Actors) Row(b, r.Scope, r.PetDisplayName ?? r.PetNormalizedName, r.DamageDealt, r.DamageReceived, M(r.PetInstanceCount), r.IsOverflow, r.CoverageLimited);
        EndTable(b, p.Actors.Count);
        Table(b, "Targets", ["Target", "Damage dealt", "Events", "Overflow"]);
        foreach (var r in p.Targets) Row(b, r.DisplayName ?? r.NormalizedTargetName, r.DamageDealt, r.EventCount, r.IsOverflow);
        EndTable(b, p.Targets.Count);

        b.Append("<h2>Build and attribution</h2><div class='cards'>");
        Card(b, "Frozen build context", $"{segment.BuildContextStatus} · {p.BuildContext.Availability}");
        Card(b, "Manifest hash", p.BuildContext.ManifestHash ?? h.BuildManifestHash ?? "NotCaptured");
        Card(b, "Capture catalog fingerprint", p.BuildContext.BuildCatalogFingerprint ?? h.BuildCatalogFingerprint ?? "NotCaptured");
        Card(b, "Proc damage", M(p.Attribution.ProcDamage));
        Card(b, "Proc contribution", M(p.Attribution.ProcContributionHundredths, v => Hundredths(v) + "%"));
        Card(b, "Parent rows incomplete", F(p.Attribution.ParentRowsIncomplete));
        b.Append("</div>");
        Table(b, "Attribution parent rows", ["Mode", "Parent power", "Proc identity", "Damage", "Events", "Evidence", "Confidence"]);
        foreach (var r in p.Attribution.ByParent) Row(b, r.Mode, r.ParentPowerName ?? r.ParentPowerId, r.ExactProcIdentity, M(r.ProcDamageMetric), r.EventCount, r.Evidence, r.Confidence);
        EndTable(b, p.Attribution.ByParent.Count);
        b.Append("<h2>Observed lifecycle telemetry</h2><div class='cards'>");
        Card(b, "Activations", M(p.Session.Metrics.ActivationCount));
        Card(b, "Confirmed recharge observations", M(p.Session.Metrics.ConfirmedRechargeCompletedCount));
        Card(b, "Still recharging observations", M(p.Session.Metrics.ConfirmedStillRechargingCount));
        Card(b, "Activation to recharge interval", M(p.Session.Metrics.ObservedActivationToRechargeInterval));
        Card(b, "Recharge to next activation interval", M(p.Session.Metrics.ObservedRechargeToNextActivationInterval));
        Card(b, "Theoretical recharge", M(p.Session.Metrics.TheoreticalRecharge));
        b.Append("</div>");
        b.Append("<details><summary>Coverage and retained detail</summary><div class='cards'>");
        Card(b, "Projection coverage limited", F(p.CoverageLimited));
        Card(b, "Historical detail", F(segment.DetailStatus));
        if (segment.Coverage is { } c)
        {
            Card(b, "Logical events", F(c.LogicalEventCount));
            Card(b, "Retained spine events", F(c.RetainedSpineEventCount));
            Card(b, "Spine retention limit", F(c.SpineRetentionLimit));
            Card(b, "Spine truncated", F(c.SpineTruncated));
            Card(b, "Coverage limited", F(c.CoverageLimited));
            b.Append("</div>");
            Table(b, "Replay coverage", ["Dimension", "Aggregate authoritative", "Replay"]);
            var r = c.Replay;
            foreach (var (label, entry) in new (string, ReplayCoverageEntry)[] {
                ("Session totals", r.SessionTotals), ("Powers", r.PerPowerTotals), ("Damage type", r.DamageType),
                ("Direct / DoT", r.DirectVersusDot), ("Actors / pets", r.ActorPet), ("Targets", r.Target),
                ("Accuracy", r.Accuracy), ("Activation", r.Activation), ("Recharge", r.LifecycleRecharge),
                ("Clock", r.Clock), ("Frozen build", r.FrozenBuildContext), ("Attribution", r.ProcAttribution), ("Event timeline", r.EventTimeline) })
                Row(b, label, entry.AggregateAuthoritative, entry.Replay);
            EndTable(b, 13);
        }
        else b.Append("</div><p>NotCaptured</p>");
        b.Append("</details><h2>Diagnostics and verification</h2><div class='cards'>");
        Card(b, "App version", h.AppVersion ?? "NotCaptured");
        Card(b, "Analytics semantic version", F(p.AnalyticsSemanticVersion));
        Card(b, "Segment / spine schema", $"{F(h.SegmentSchemaVersion)} / {F(h.SpineSchemaVersion)}");
        Card(b, "Legacy observation schema", F(h.ObservationSchemaVersion));
        Card(b, "Grammar / dedup policy", $"{F(h.GrammarSetVersion)} / {F(h.DedupPolicyVersion)}");
        Card(b, "Attribution policy", F(h.AttributionPolicyVersion));
        Card(b, "Applied logical events", F(p.LogicalEventsApplied));
        Card(b, "Duplicate occurrences ignored", F(p.DuplicateOccurrencesIgnored));
        Card(b, "Parser diagnostics / raw samples", "NotCaptured — not persisted in historical Segments");
        Card(b, "Annotations", F(segment.AnnotationStatus));
        Card(b, "Observed analytical span (diagnostic)", M(p.Clock.ObservedAnalyticalSpan));
        b.Append("</div></main><footer>Historical reference · authoritative stored projection · no log replay or current-build reinterpretation</footer></body></html>");
        return b.ToString();
    }

    private static string Hundredths(long value) => (value / 100m).ToString("0.##", CultureInfo.InvariantCulture);
    private static string F(object? value) => value is null ? "NotCaptured" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NotCaptured";
    private static string E(object? value) => WebUtility.HtmlEncode(F(value));
    private static string M<T>(Metric<T> metric, Func<T, string>? format = null) where T : struct =>
        metric.Value is { } value
            ? $"{(format is null ? F(value) : format(value))} · {metric.Availability} · {metric.Evidence}"
              + (metric.Denominator is { } d ? $" · {d}" : "")
              + (metric.Confidence is { } confidence ? $" · {confidence}" : "")
              + (metric.Coverage is { } c ? $" · {c}" : "")
            : metric.Availability.ToString();
    private static string Accuracy(MetricRef<CombatAccuracyScopeSnapshot> metric) => metric.Value is { } v
        ? $"{metric.Availability} · {v.Hits} hits / {v.Misses} misses / {v.Attempts} attempts · {metric.Evidence}"
          + (metric.Coverage is { } c ? $" · {c}" : "")
        : metric.Availability.ToString();
    private static void Card(StringBuilder b, string label, string value) => b.Append("<article><h3>").Append(E(label)).Append("</h3><p>").Append(E(value)).Append("</p></article>");
    private static void Table(StringBuilder b, string title, string[] columns)
    {
        b.Append("<section><h2>").Append(E(title)).Append("</h2><div class='table-wrap'><table><thead><tr>");
        foreach (var column in columns) b.Append("<th scope='col'>").Append(E(column)).Append("</th>");
        b.Append("</tr></thead><tbody>");
    }
    private static void Row(StringBuilder b, params object?[] cells)
    {
        b.Append("<tr>");
        foreach (var cell in cells) b.Append("<td>").Append(E(cell)).Append("</td>");
        b.Append("</tr>");
    }
    private static void EndTable(StringBuilder b, int count)
    {
        b.Append("</tbody></table></div>");
        if (count == 0) b.Append("<p class='muted'>No rows supplied.</p>");
        b.Append("</section>");
    }
    private static void Types(StringBuilder b, string title, MetricRef<IReadOnlyList<CombatDamageTypeTotal>> metric)
    {
        b.Append("<p>").Append(E(title)).Append(": ").Append(E(metric.Availability)).Append(" · ").Append(E(metric.Evidence));
        if (metric.Coverage is { } coverage) b.Append(" · ").Append(E(coverage));
        b.Append("</p>");
        Table(b, title, ["Type", "Amount", "Events", "Overflow"]);
        if (metric.Value is { } rows) foreach (var r in rows) Row(b, r.DamageType, r.Amount, r.EventCount, r.IsOverflow);
        EndTable(b, metric.Value?.Count ?? 0);
    }

    private const string DocumentStart = """
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
        <title>CoH Analytics — Segment Reference</title><style>
        /* Presentation adapted from CoH_Analytics_Architecture_Aligned_Reference.html. */
        :root{color-scheme:dark;--bg:#071019;--panel:#0d1b28;--line:#1e4258;--text:#e8f3f8;--muted:#89a8b8;--cyan:#37d5ff}
        *{box-sizing:border-box}html{background:linear-gradient(180deg,#08131d,#050b11);color:var(--text);font-family:Segoe UI,Inter,system-ui,sans-serif}
        body{max-width:1450px;margin:auto;padding:18px 22px 46px}.topbar,.card,article,.table-wrap{background:linear-gradient(180deg,rgba(15,36,51,.98),rgba(10,26,38,.98));border:1px solid #1b4055;box-shadow:0 12px 30px rgba(0,0,0,.24)}
        .topbar{display:flex;gap:12px;align-items:center;padding:14px 18px;border-radius:14px 14px 0 0}.brandmark{width:42px;height:42px;display:grid;place-items:center;border:1px solid var(--cyan);border-radius:10px;font-weight:800}
        h1{font-size:19px;margin:0}.topbar p{margin:4px 0 0}.context{padding:15px 17px;margin-top:15px;border-radius:12px}.context h2{margin:6px 0 3px;font-size:21px}
        h2{font-size:17px;margin-top:28px}h3,.eyebrow{font-size:11px;text-transform:uppercase;letter-spacing:.12em;color:#6e99aa}
        .cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px}article{border-radius:12px;padding:13px 14px;overflow-wrap:anywhere}article h3{margin:0}article p{font-size:16px;margin:8px 0 0}
        .table-wrap{overflow:auto;border-radius:12px;padding:8px}table{border-collapse:collapse;width:100%}th{text-align:left;font-size:10px;text-transform:uppercase;letter-spacing:.08em;color:#7295a5;border-bottom:1px solid #214456;padding:9px 8px}td{font-size:12px;color:#cfe2e9;border-bottom:1px solid #153443;padding:9px 8px;vertical-align:top;overflow-wrap:anywhere}
        details{margin-top:28px;border:1px solid #1d4053;border-radius:9px;padding:14px;background:#0a1b27}summary{cursor:pointer;color:#bcd5df;margin-bottom:12px}footer,.muted{color:var(--muted);font-size:12px}footer{padding:18px 0;border-top:1px solid #153443;margin-top:18px}.context p{overflow-wrap:anywhere}
        @media(max-width:760px){body{padding:10px}.cards{grid-template-columns:repeat(2,minmax(0,1fr))}}@media print{body{max-width:none}article,.table-wrap{box-shadow:none}}
        </style></head><body>
        """;
}
