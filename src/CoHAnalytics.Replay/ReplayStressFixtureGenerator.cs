using System.Security.Cryptography;
using System.Text;

namespace CoHAnalytics.Replay;

public sealed class ReplayStressFixtureGenerator
{
    private static readonly string[] FictionalHeroNames =
    [
        "Fictional Hero Alpha",
        "Fictional Hero Beta",
        "Fictional Hero Gamma",
        "Fictional Hero Delta"
    ];

    private static readonly string[] FictionalTargetPrefixes =
    [
        "Fictional Target",
        "Fictional Nemesis",
        "Fictional Lieutenant",
        "Fictional Boss"
    ];

    private static readonly string[] DamageTypes =
    [
        "smashing",
        "lethal",
        "fire",
        "cold",
        "energy",
        "negative energy",
        "toxic",
        "psionic"
    ];

    private static readonly string[] FictionalPowers =
    [
        "Fictional Strike",
        "Fictional Blast",
        "Fictional Shield",
        "Fictional Heal",
        "Fictional Toggle"
    ];

    private static readonly string[] FictionalZones =
    [
        "Fictional Plaza",
        "Fictional Sector",
        "Fictional District"
    ];

    public static string ComputeDeterministicHash(ReplayStressWorkloadDefinition definition)
    {
        var payload = $"{definition.Seed}:{definition.LineCount}:{definition.FanOut}:{definition.ContextId}:{definition.SegmentCount}:{definition.IncludeWelcome}:{definition.EnableLatencyMarkers}:{definition.LatencyMarkerEveryLines}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    public static async Task<string> GenerateToFileAsync(
        ReplayStressWorkloadDefinition definition,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
        await foreach (var line in EnumerateLinesAsync(definition, cancellationToken).ConfigureAwait(false))
        {
            await writer.WriteAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    public static async IAsyncEnumerable<string> EnumerateLinesAsync(
        ReplayStressWorkloadDefinition definition,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var random = new Random(definition.Seed);
        var heroName = FictionalHeroNames[definition.ContextId % FictionalHeroNames.Length];
        var baseTime = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Unspecified);
        var lineIndex = 0;
        var markerId = 1;

        if (definition.IncludeWelcome)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return FormatLine(baseTime, $"Welcome to City of Heroes, {heroName}!");
            lineIndex++;
        }

        yield return FormatLine(baseTime.AddSeconds(1), $"You are now entering {FictionalZones[definition.ContextId % FictionalZones.Length]}.");

        for (var index = 0; index < definition.LineCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineIndex++;

            if (definition.EnableLatencyMarkers
                && definition.LatencyMarkerEveryLines > 0
                && lineIndex % definition.LatencyMarkerEveryLines == 0)
            {
                yield return ReplayLatencyTracker.FormatMarkerLine(
                    baseTime.AddMilliseconds(lineIndex * 10),
                    markerId++);
            }

            var template = random.Next(0, 12);
            var timestamp = baseTime.AddMilliseconds(lineIndex * 10);
            var targetIndex = random.Next(0, Math.Max(1, definition.FanOut));
            var targetName = $"{FictionalTargetPrefixes[targetIndex % FictionalTargetPrefixes.Length]} {targetIndex + 1}";
            var damage = random.Next(5, 120);
            var damageType = DamageTypes[random.Next(DamageTypes.Length)];

            var line = template switch
            {
                0 => $"You hit {targetName} for {damage} points of {damageType} damage.",
                1 => $"{targetName} hits you for {damage} points of {damageType} damage.",
                2 => $"You activate {FictionalPowers[random.Next(FictionalPowers.Length)]}.",
                3 => $"{targetName} is held by Fictional Hold.",
                4 => $"{targetName} is knocked down.",
                5 => $"You recover {random.Next(5, 25)} points of Endurance.",
                6 => $"You heal {heroName} for {damage} hit points.",
                7 => $"{heroName} defeated {targetName}.",
                8 => $"You receive {random.Next(10, 100)} experience.",
                9 => $"You have gained {random.Next(1, 20)} influence.",
                10 => $"You receive a reward: Fictional Item Token {random.Next(1, 50)}.",
                _ => $"Toggle '{FictionalPowers[random.Next(FictionalPowers.Length)]}' is now {(random.Next(0, 2) == 0 ? "on" : "off")}."
            };

            if (random.Next(0, 20) == 0)
            {
                yield return FormatLine(timestamp, line);
                yield return FormatLine(timestamp, $"*** Fictional proc effect on {targetName}.");
            }
            else
            {
                yield return FormatLine(timestamp, line);
            }
        }
    }

    public static IEnumerable<string> EnumerateManifestTokens(ReplayStressWorkloadDefinition definition)
    {
        yield return "Fictional";
        yield return "Welcome to City of Heroes";
        yield return FictionalHeroNames[definition.ContextId % FictionalHeroNames.Length];
        yield return FictionalZones[definition.ContextId % FictionalZones.Length];
        foreach (var prefix in FictionalTargetPrefixes)
        {
            yield return prefix;
        }
    }

    private static string FormatLine(DateTime timestamp, string body) =>
        $"{timestamp:yyyy-MM-dd HH:mm:ss} {body}\r\n";
}
