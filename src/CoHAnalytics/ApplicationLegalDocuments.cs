namespace CoHAnalytics;

public static class ApplicationLegalDocuments
{
    public const string LicenseFileName = "LICENSE";

    public const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.md";

    public const string RepositoryLicenseUri =
        "https://github.com/WITSLLC/CoH-Analytics/blob/main/LICENSE";

    public static bool TryResolveLicensePath(out string path) =>
        TryResolveLicensePath(AppContext.BaseDirectory, out path);

    internal static bool TryResolveLicensePath(string startDirectory, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return false;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, LicenseFileName);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }

            // Source-tree builds often run from bin/<config>/<tfm>; keep walking toward the repo root.
            directory = directory.Parent;
        }

        return false;
    }
}
