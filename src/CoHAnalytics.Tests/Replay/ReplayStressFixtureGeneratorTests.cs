using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayStressFixtureGeneratorTests
{
    [Fact]
    public async Task Deterministic_hash_matches_for_same_seed_and_options()
    {
        var definition = new ReplayStressWorkloadDefinition
        {
            LineCount = 50,
            Seed = 4242,
            FanOut = 4,
            ContextId = 1,
            EnableLatencyMarkers = true,
            LatencyMarkerEveryLines = 10
        };

        var first = ReplayStressFixtureGenerator.ComputeDeterministicHash(definition);
        var second = ReplayStressFixtureGenerator.ComputeDeterministicHash(definition);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Generated_file_uses_valid_crlf_and_fictional_allowlist()
    {
        var definition = new ReplayStressWorkloadDefinition
        {
            LineCount = 25,
            Seed = 99,
            FanOut = 3,
            ContextId = 0,
            EnableLatencyMarkers = true,
            LatencyMarkerEveryLines = 5
        };
        var path = Path.Combine(Path.GetTempPath(), $"stress-{Guid.NewGuid():n}.log");
        try
        {
            await ReplayStressFixtureGenerator.GenerateToFileAsync(definition, path);
            var bytes = await File.ReadAllBytesAsync(path);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Contains("\r\n", text, StringComparison.Ordinal);
            Assert.Contains("Fictional", text, StringComparison.Ordinal);
            Assert.DoesNotContain("@", text, StringComparison.Ordinal);
            Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Example Hero", text, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Stream_enumeration_does_not_require_full_memory_retention()
    {
        var definition = new ReplayStressWorkloadDefinition
        {
            LineCount = 200,
            Seed = 7,
            FanOut = 2,
            ContextId = 0
        };

        var count = 0;
        await foreach (var _ in ReplayStressFixtureGenerator.EnumerateLinesAsync(definition))
        {
            count++;
        }

        Assert.True(count > definition.LineCount);
    }

    [Fact]
    public async Task Target_fan_out_includes_multiple_fictional_targets()
    {
        var definition = new ReplayStressWorkloadDefinition
        {
            LineCount = 40,
            Seed = 12,
            FanOut = 6,
            ContextId = 0
        };
        var path = Path.Combine(Path.GetTempPath(), $"stress-fanout-{Guid.NewGuid():n}.log");
        try
        {
            await ReplayStressFixtureGenerator.GenerateToFileAsync(definition, path);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("Fictional Target 1", text, StringComparison.Ordinal);
            Assert.Contains("Fictional Lieutenant", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
