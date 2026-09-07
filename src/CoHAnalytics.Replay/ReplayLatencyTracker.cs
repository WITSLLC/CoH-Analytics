using System.Globalization;
using System.Text.RegularExpressions;

namespace CoHAnalytics.Replay;

public sealed class ReplayLatencyTracker
{
    public const string MarkerToken = "[System] Replay marker ";

    private static readonly Regex MarkerPattern = new(
        @"\[System\] Replay marker (\d{6})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly TimeProvider _timeProvider;
    private readonly int _maxSamples;
    private readonly Dictionary<int, long> _writeTimestamps = [];
    private readonly List<double> _parserLatenciesMilliseconds = [];
    private readonly List<double> _gameplayLatenciesMilliseconds = [];
    private readonly object _sync = new();
    private int _duplicateMarkerCount;
    private int _missingWriteCount;

    public ReplayLatencyTracker(TimeProvider timeProvider, int maxSamples = 64)
    {
        _timeProvider = timeProvider;
        _maxSamples = maxSamples;
    }

    public bool Enabled { get; set; } = true;

    public int DuplicateMarkerCount => _duplicateMarkerCount;

    public int MissingWriteCount => _missingWriteCount;

    public static string FormatMarkerLine(DateTime timestamp, int markerId) =>
        $"{timestamp:yyyy-MM-dd HH:mm:ss} {MarkerToken}{markerId:D6}\r\n";

    public static bool TryParseMarkerId(string line, out int markerId)
    {
        var match = MarkerPattern.Match(line);
        if (!match.Success)
        {
            markerId = 0;
            return false;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out markerId);
    }

    public void RecordMarkerWritten(int markerId)
    {
        if (!Enabled)
        {
            return;
        }

        var timestamp = _timeProvider.GetTimestamp();
        lock (_sync)
        {
            if (_writeTimestamps.ContainsKey(markerId))
            {
                _duplicateMarkerCount++;
            }

            _writeTimestamps[markerId] = timestamp;
        }
    }

    public void ObserveRawLine(string line)
    {
        if (!Enabled || !TryParseMarkerId(line, out var markerId))
        {
            return;
        }

        ObserveMarker(markerId, isGameplay: false);
    }

    public void ObserveCommittedLine(string line)
    {
        if (!Enabled || !TryParseMarkerId(line, out var markerId))
        {
            return;
        }

        ObserveMarker(markerId, isGameplay: true);
    }

    private void ObserveMarker(int markerId, bool isGameplay)
    {
        var observeTimestamp = _timeProvider.GetTimestamp();
        lock (_sync)
        {
            if (!_writeTimestamps.TryGetValue(markerId, out var writeTimestamp))
            {
                _missingWriteCount++;
                return;
            }

            var latencyMs = ToMilliseconds(_timeProvider.GetElapsedTime(writeTimestamp, observeTimestamp));
            if (isGameplay)
            {
                AddBounded(_gameplayLatenciesMilliseconds, latencyMs);
            }
            else
            {
                AddBounded(_parserLatenciesMilliseconds, latencyMs);
            }
        }
    }

    public ReplayLatencyAggregateReport BuildReport()
    {
        lock (_sync)
        {
            var parserReport = BuildChannelReport(_parserLatenciesMilliseconds);
            var gameplayReport = BuildChannelReport(_gameplayLatenciesMilliseconds);
            var combined = _parserLatenciesMilliseconds.Concat(_gameplayLatenciesMilliseconds).ToList();
            var combinedReport = BuildChannelReport(combined);

            return new ReplayLatencyAggregateReport
            {
                Count = combinedReport.Count,
                P50Milliseconds = combinedReport.P50,
                P95Milliseconds = combinedReport.P95,
                P99Milliseconds = combinedReport.P99,
                MaxMilliseconds = combinedReport.Max,
                ParserCount = parserReport.Count,
                ParserP50Milliseconds = parserReport.P50,
                ParserP95Milliseconds = parserReport.P95,
                ParserP99Milliseconds = parserReport.P99,
                ParserMaxMilliseconds = parserReport.Max,
                GameplayCount = gameplayReport.Count,
                GameplayP50Milliseconds = gameplayReport.P50,
                GameplayP95Milliseconds = gameplayReport.P95,
                GameplayP99Milliseconds = gameplayReport.P99,
                GameplayMaxMilliseconds = gameplayReport.Max
            };
        }
    }

    internal static (long Count, double? P50, double? P95, double? P99, double? Max) BuildChannelReport(
        IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return (0, null, null, null, null);
        }

        var sorted = values.OrderBy(value => value).ToArray();
        return (
            sorted.Length,
            ComputePercentile(sorted, 0.50),
            ComputePercentile(sorted, 0.95),
            ComputePercentile(sorted, 0.99),
            sorted[^1]);
    }

    public static double? ComputePercentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 0)
        {
            return null;
        }

        if (percentile <= 0)
        {
            return sortedAscending[0];
        }

        if (percentile >= 1)
        {
            return sortedAscending[^1];
        }

        var index = percentile * (sortedAscending.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sortedAscending[lower];
        }

        var weight = index - lower;
        return sortedAscending[lower] + (sortedAscending[upper] - sortedAscending[lower]) * weight;
    }

    private void AddBounded(List<double> target, double value)
    {
        if (target.Count >= _maxSamples)
        {
            target.RemoveAt(0);
        }

        target.Add(value);
    }

    private static double ToMilliseconds(TimeSpan elapsed) => elapsed.TotalMilliseconds;
}
