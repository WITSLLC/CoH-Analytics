using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class InternalFeatureGateTests
{
    [Fact]
    public void Developer_mode_defaults_off_when_AngiesView_is_absent()
    {
        using var harness = new SettingsHarness(angiesView: null);
        var gate = new InternalFeatureGate(harness.SettingsService);

        Assert.False(gate.IsDeveloperMode);
    }

    [Fact]
    public void Developer_mode_is_off_for_non_matching_token()
    {
        using var harness = new SettingsHarness(angiesView: "not-ellie");
        var gate = new InternalFeatureGate(harness.SettingsService);

        Assert.False(gate.IsDeveloperMode);
    }

    [Fact]
    public void Developer_mode_is_on_only_for_exact_Ellie_token()
    {
        using var harness = new SettingsHarness(angiesView: "Ellie");
        var gate = new InternalFeatureGate(harness.SettingsService);

        Assert.True(gate.IsDeveloperMode);
    }

    [Fact]
    public void Developer_mode_is_case_sensitive()
    {
        using var harness = new SettingsHarness(angiesView: "ellie");
        var gate = new InternalFeatureGate(harness.SettingsService);

        Assert.False(gate.IsDeveloperMode);
    }

    private sealed class SettingsHarness : IDisposable
    {
        private readonly string _directory;

        public SettingsHarness(string? angiesView)
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "CoHAnalytics-InternalFeatureGateTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SettingsService = new SettingsService(_directory);
            SettingsService.Save(new AppSettings { AngiesView = angiesView });
        }

        public SettingsService SettingsService { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
