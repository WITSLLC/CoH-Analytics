using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Assembles <see cref="CombatPipelineDiagnostics"/> from already-classified parser events and
/// optional later-stage results. Observation only: no CombatEngine Apply, no catalog, no persist.
/// </summary>
public static partial class CombatPipelineDiagnosticsFactory
{
    public static CombatPipelineDiagnostics Observe(
        IReadOnlyList<ParserEvent> lines,
        DedupDiagnostics? dedup = null,
        CombatAnalyticsProjection? projection = null,
        SegmentCoverageDescriptor? coverage = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var parser = new CombatEventParser();
        var classified = 0L;
        var canonicalParsed = 0L;
        var legacyUnparsed = 0L;
        var canonicalUnparsed = 0L;
        var samples = new List<SanitizedUnparsedSample>(CombatPipelineDiagnostics.MaxUnparsedSamples);
        var channels = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            classified++;
            if (!string.IsNullOrEmpty(line.SourceChannel))
            {
                var channel = SanitizeChannel(line.SourceChannel);
                channels.Add(channel);
                if (channels.Count > CombatPipelineDiagnostics.MaxObservedSourceChannels)
                {
                    channels.Remove(channels.Max!);
                }
            }

            var parsedCanonical = parser.TryParseCanonical(line, out var canonical);
            if (parsedCanonical)
            {
                canonicalParsed++;
            }

            var legacyParsed = parsedCanonical && parser.TryAdaptToLegacy(canonical, out _);
            if (CombatEventParser.IsCombatShapedUnparsed(line, legacyParsed))
            {
                legacyUnparsed++;
            }

            if (CombatEventParser.IsCanonicalCombatUnparsed(line, parsedCanonical))
            {
                canonicalUnparsed++;
                if (samples.Count < CombatPipelineDiagnostics.MaxUnparsedSamples
                    && ParserLineEnvelope.TryGetBody(line.RawLine, line.SourceId.LogDate, out var body))
                {
                    samples.Add(new SanitizedUnparsedSample
                    {
                        EventKind = line.EventKind,
                        ClassificationRuleId = line.ClassificationRuleId,
                        SourceChannel = string.IsNullOrEmpty(line.SourceChannel)
                            ? line.SourceChannel
                            : SanitizeChannel(line.SourceChannel),
                        SanitizedBody = SanitizeBody(body)
                    });
                }
            }
        }

        var attribution = projection?.Attribution ?? CombatProcAttributionSummary.Empty;
        return new CombatPipelineDiagnostics
        {
            ClassifiedLineCount = classified,
            CanonicalParsedCount = canonicalParsed,
            LegacyCombatShapedUnparsedCount = legacyUnparsed,
            CanonicalUnparsedCount = canonicalUnparsed,
            Dedup = dedup ?? DedupDiagnostics.Empty(DedupPolicyVersion.Current),
            Attribution = new AttributionModeHistogram
            {
                Direct = attribution.DirectCount,
                BuildConfirmed = attribution.BuildConfirmedCount,
                Correlated = attribution.CorrelatedCount,
                Unattributed = attribution.UnattributedCount
            },
            CoverageLimited = projection?.CoverageLimited == true
                || coverage?.CoverageLimited == true
                || projection?.Session.Metrics.DistinctTargetCount.Coverage?.Overflow == true,
            TargetOverflow = projection?.Targets.Any(row => row.IsOverflow) == true
                || projection?.Session.Metrics.DistinctTargetCount.Coverage?.Overflow == true,
            MissingOutgoingDamageType = projection?.DamageTypeBreakdown.Coverage?.MissingDamageType == true,
            MissingIncomingDamageType = projection?.IncomingDamageTypeBreakdown.Coverage?.MissingDamageType == true,
            TargetLowerBound = projection?.Session.Metrics.DistinctTargetCount.Coverage?.LowerBound == true,
            PetNameRollup = projection?.Session.Metrics.DamageDealtOwnedPets.Coverage?.PetNameRollup == true
                || projection?.Session.Metrics.DamageReceivedOwnedPets.Coverage?.PetNameRollup == true,
            Replay = coverage?.Replay,
            ObservedSourceChannels = channels.ToArray(),
            CanonicalUnparsedSamples = samples,
            AnalyticsSemanticVersion = projection?.AnalyticsSemanticVersion ?? AnalyticsSemanticVersion.Current,
            GrammarSetVersion = EventProvenance.CurrentGrammarSetVersion,
            DedupPolicyVersion = DedupPolicyVersion.Current,
            AttributionPolicyVersion = projection?.Attribution.AttributionPolicyVersion
                ?? AttributionPolicyVersion.Current,
            SegmentSchemaVersion = Models.SegmentSchemaVersion.Current,
            SpineSchemaVersion = Models.SpineSchemaVersion.Current
        };
    }

    internal static string SanitizeBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var sanitized = PathLike().Replace(body, "[path]");
        return sanitized.Length <= CombatPipelineDiagnostics.MaxSampleBodyChars
            ? sanitized
            : sanitized[..CombatPipelineDiagnostics.MaxSampleBodyChars];
    }

    private static string SanitizeChannel(string channel)
    {
        var sanitized = PathLike().Replace(channel, "[path]");
        return sanitized.Length <= CombatPipelineDiagnostics.MaxSourceChannelChars
            ? sanitized
            : sanitized[..CombatPipelineDiagnostics.MaxSourceChannelChars];
    }

    [GeneratedRegex(
        @"[""'](?:[A-Za-z]:[\\/]|\\\\|//)[^""'\r\n]+[""']|(?:[A-Za-z]:[\\/]|\\\\[^\\/\r\n]+[\\/]|//[^/\r\n]+/).*?\.(?:txt|log|jsonl?|db|tsv|csv)(?=$|[\s.,;:!?])|(?:[A-Za-z]:[\\/]|\\\\|//)[^\s""'<>|]*|[\\/]accounts[\\/][^\s""'<>|]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PathLike();
}
