using System.Diagnostics;
using CoHAnalytics.Tests.Replay;
using Xunit.Abstractions;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatEventParserThroughputTests(ITestOutputHelper output)
{
    [Fact]
    public void Combat_core_grammar_fixture_reports_parser_throughput_baseline()
    {
        var coreFixturePath = ReplayTestPaths.Fixture("combat-core-grammar.log");
        var accuracyFixturePath = ReplayTestPaths.Fixture("combat-accuracy-grammar.log");
        var coreLines = File.ReadAllLines(coreFixturePath);
        var accuracyLines = File.ReadAllLines(accuracyFixturePath);
        const int iterations = 5_000;

        var stopwatch = Stopwatch.StartNew();
        var combatEvents = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            combatEvents += CombatEventParserTestSupport.ParseFixtureLines(coreLines, new DateOnly(2026, 8, 4)).Count;
            combatEvents += CombatEventParserTestSupport.ParseFixtureLines(accuracyLines, new DateOnly(2026, 8, 6)).Count;
        }

        stopwatch.Stop();

        var totalLines = (coreLines.Length + accuracyLines.Length) * iterations;
        var linesPerSecond = totalLines / stopwatch.Elapsed.TotalSeconds;
        var eventsPerSecond = combatEvents / stopwatch.Elapsed.TotalSeconds;

        Assert.Equal(14, CombatEventParserTestSupport.ParseFixtureLines(coreLines, new DateOnly(2026, 8, 4)).Count);
        Assert.Equal(11, CombatEventParserTestSupport.ParseFixtureLines(accuracyLines, new DateOnly(2026, 8, 6)).Count);
        Assert.True(linesPerSecond > 10_000, $"Expected at least 10k lines/sec, observed {linesPerSecond:N0}");

        ThroughputBaseline = new CombatParserThroughputBaseline(
            LinesParsed: totalLines,
            CombatEventsProduced: combatEvents,
            Elapsed: stopwatch.Elapsed,
            LinesPerSecond: linesPerSecond,
            EventsPerSecond: eventsPerSecond);

        output.WriteLine(
            $"Combat parser throughput baseline: lines={totalLines:N0}, events={combatEvents:N0}, " +
            $"elapsed={stopwatch.Elapsed.TotalMilliseconds:N0}ms, lines/sec={linesPerSecond:N0}, events/sec={eventsPerSecond:N0}");
    }

    internal static CombatParserThroughputBaseline? ThroughputBaseline { get; private set; }

    internal sealed record CombatParserThroughputBaseline(
        int LinesParsed,
        int CombatEventsProduced,
        TimeSpan Elapsed,
        double LinesPerSecond,
        double EventsPerSecond);
}
