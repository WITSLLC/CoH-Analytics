using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayFixturePrivacyTests
{
    private static readonly Regex DrivePathPattern = new(@"[A-Za-z]:\\", RegexOptions.Compiled);
    private static readonly Regex UncPathPattern = new(@"\\\\", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GlobalHandlePattern = new(@"@\w+", RegexOptions.Compiled);

    [Fact]
    public void Manifest_lists_every_committed_fixture_and_hashes_match()
    {
        var fixturesRoot = ReplayTestPaths.FixturesRoot;
        var manifestPath = ReplayTestPaths.Fixture("fixtures.v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifestFiles = document.RootElement.GetProperty("fixtures")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("fileName").GetString()!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var committedFiles = Directory.GetFiles(fixturesRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "fixtures.v1.json", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(committedFiles, manifestFiles);

        foreach (var fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var fileName = fixture.GetProperty("fileName").GetString()!;
            var path = ReplayTestPaths.Fixture(fileName);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(fixture.GetProperty("byteLength").GetInt64(), bytes.LongLength);

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.Equal(fixture.GetProperty("sha256").GetString(), hash);
        }
    }

    [Fact]
    public void Fixtures_contain_no_privacy_sensitive_content()
    {
        var manifestPath = ReplayTestPaths.Fixture("fixtures.v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var allowList = document.RootElement.GetProperty("allowedFictionalIdentifiers")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var fileName = fixture.GetProperty("fileName").GetString()!;
            var path = ReplayTestPaths.Fixture(fileName);
            var spans = GetInspectableSpans(path);
            foreach (var span in spans)
            {
                Assert.False(DrivePathPattern.IsMatch(span), $"Drive path found in {fileName}");
                Assert.False(UncPathPattern.IsMatch(span), $"UNC path found in {fileName}");
                Assert.False(EmailPattern.IsMatch(span), $"Email found in {fileName}");
                Assert.False(UrlPattern.IsMatch(span), $"URL found in {fileName}");
                Assert.False(GlobalHandlePattern.IsMatch(span), $"Global handle found in {fileName}");
                Assert.DoesNotContain("Users\\", span, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("homecoming", span, StringComparison.OrdinalIgnoreCase);

                foreach (Match match in Regex.Matches(span, @"[A-Z][a-z]+ [A-Z][a-z]+"))
                {
                    if (!allowList.Contains(match.Value))
                    {
                        Assert.Fail($"Unexpected identifier '{match.Value}' in {fileName}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Repository_does_not_commit_private_replay_input_directory()
    {
        var repoRoot = LocateRepositoryRoot();
        Assert.False(Directory.Exists(Path.Combine(repoRoot, ".replay-data")));
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "replay-output")));
    }

    private static IEnumerable<string> GetInspectableSpans(string path)
    {
        if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = File.ReadAllBytes(path);
            yield return Encoding.UTF8.GetString(bytes.Where(value => value is >= 32 and <= 126 or (byte)'\r' or (byte)'\n').ToArray());
            yield break;
        }

        yield return File.ReadAllText(path);
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "CoHAnalytics.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
