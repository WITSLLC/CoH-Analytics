using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

internal static class CombatEventParserTestSupport
{
    internal static readonly ParserClassifier Classifier = new();

    internal static readonly CombatEventParser Parser = new();

    internal static bool TryParseLine(
        string line,
        out CombatEvent combatEvent,
        long sequence = 1,
        DateOnly? logDate = null,
        MonitoringContextId? contextId = null) =>
        Parser.TryParse(
            Classify(line, sequence, logDate, contextId),
            out combatEvent);

    internal static ParserEvent Classify(
        string line,
        long sequence = 1,
        DateOnly? logDate = null,
        MonitoringContextId? contextId = null)
    {
        var resolvedContextId = contextId ?? MonitoringContextId.CreateNew();
        var resolvedLogDate = logDate ?? new DateOnly(2026, 8, 4);
        return Classifier.Classify(new ParserRawEvent
        {
            ContextId = resolvedContextId,
            SourceId = LogSourceId.Create(
                "acct-1",
                "acct-1",
                "C:\\fake\\chatlog.txt",
                resolvedLogDate),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            Sequence = sequence,
            ObservedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence - 1),
            RawLine = line,
            SourceByteStart = 0,
            SourceByteEnd = line.Length,
            LineStatus = ParserLineStatus.Complete
        });
    }

    internal static IReadOnlyList<CombatEvent> ParseFixtureLines(
        IEnumerable<string> lines,
        DateOnly logDate,
        MonitoringContextId? contextId = null)
    {
        var events = new List<CombatEvent>();
        var sequence = 1L;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseLine(
                    line,
                    out var combatEvent,
                    sequence,
                    logDate,
                    contextId))
            {
                events.Add(combatEvent);
            }

            sequence++;
        }

        return events;
    }

    internal static void AssertEquivalent(CombatEvent expected, CombatEvent actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.GrammarId, actual.GrammarId);
        Assert.Equal(expected.ActorRole, actual.ActorRole);
        Assert.Equal(expected.TargetName, actual.TargetName);
        Assert.Equal(expected.SourceName, actual.SourceName);
        Assert.Equal(expected.PowerName, actual.PowerName);
        Assert.Equal(expected.Amount, actual.Amount);
        Assert.Equal(expected.DamageType, actual.DamageType);
        Assert.Equal(expected.IsOverTime, actual.IsOverTime);
        Assert.Equal(expected.EffectSuffix, actual.EffectSuffix);
        Assert.Equal(expected.AttackOutcome, actual.AttackOutcome);
        Assert.Equal(expected.DisplayedChanceHundredths, actual.DisplayedChanceHundredths);
        Assert.Equal(expected.RollHundredths, actual.RollHundredths);
        Assert.Equal(expected.WasRolled, actual.WasRolled);
        Assert.Equal(expected.WasForced, actual.WasForced);
        Assert.Equal(expected.IsAutohit, actual.IsAutohit);
        Assert.Equal(expected.ParserSequence, actual.ParserSequence);
        Assert.Equal(expected.SourceTimestamp, actual.SourceTimestamp);
    }
}
