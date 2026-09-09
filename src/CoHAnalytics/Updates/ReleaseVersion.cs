using System.Globalization;
using System.Text.RegularExpressions;

namespace CoHAnalytics.Updates;

public sealed record ReleaseVersion(int Major, int Minor, int Patch, bool IsBeta) : IComparable<ReleaseVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^(?:v)?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-beta)?$",
        RegexOptions.CultureInvariant);

    public string CanonicalText => $"{Major}.{Minor}.{Patch}{(IsBeta ? "-beta" : string.Empty)}";

    public string TagText => $"v{CanonicalText}";

    public string DisplayText => $"{Major}.{Minor}.{Patch}{(IsBeta ? " Beta" : string.Empty)}";

    public static bool TryParse(string? value, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern.Match(value.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, match.Groups[4].Success);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0)
        {
            return comparison;
        }

        if (IsBeta == other.IsBeta)
        {
            return 0;
        }

        return IsBeta ? -1 : 1;
    }
}
