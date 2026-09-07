namespace CoHAnalytics.Services;

public static class AssetUri
{
    public static Uri ForResource(string relativePath)
    {
        return new Uri($"pack://application:,,,/{relativePath.Replace('\\', '/')}", UriKind.Absolute);
    }
}
