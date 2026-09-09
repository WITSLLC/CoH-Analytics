using CoHAnalytics.Updates;

namespace CoHAnalytics.Tests.Updates;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.2-beta", "0.1.3-beta")]
    [InlineData("0.1.9-beta", "0.2.0-beta")]
    [InlineData("0.9.9-beta", "1.0.0")]
    [InlineData("0.1.2-beta", "0.1.2")]
    public void Comparison_uses_numeric_semver_precedence(string olderText, string newerText)
    {
        Assert.True(ReleaseVersion.TryParse(olderText, out var older));
        Assert.True(ReleaseVersion.TryParse(newerText, out var newer));

        Assert.True(older!.CompareTo(newer) < 0);
        Assert.True(newer!.CompareTo(older) > 0);
    }

    [Theory]
    [InlineData("0.1.2-rc")]
    [InlineData("v0.1-beta.1")]
    [InlineData("0.1.2-Beta")]
    [InlineData("01.1.2-beta")]
    [InlineData("0.1")]
    [InlineData("")]
    public void Unsupported_versions_are_rejected(string value)
    {
        Assert.False(ReleaseVersion.TryParse(value, out _));
    }

    [Theory]
    [InlineData("0.1.2-beta", "0.1.2-beta", "0.1.2 Beta")]
    [InlineData("v1.0.0", "1.0.0", "1.0.0")]
    public void Parser_produces_canonical_and_display_text(string value, string canonical, string display)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var version));

        Assert.Equal(canonical, version!.CanonicalText);
        Assert.Equal(display, version.DisplayText);
    }
}
