using System.IO;
using System.Text.Json;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>Validated live Homecoming inputs for developer-time static-data import.</summary>
internal sealed record HomecomingStaticDataSource
{
    public required string InstallRoot { get; init; }

    public required string BinPiggPath { get; init; }

    public required string BinPowersPiggPath { get; init; }

    public required string BuildVersion { get; init; }

    public required string PackageRevision { get; init; }
}

/// <summary>Locates and validates the installed files required by later importer slices.</summary>
internal static class HomecomingStaticDataSourceDiscovery
{
    internal const string LivePackageMetadataRelativePath = @"settings\launcher\packages\hc_live.json";
    internal const string LiveDataPackageMetadataRelativePath = @"settings\launcher\packages\hc_data_live.json";
    internal const string BinPiggRelativePath = @"assets\live\bin.pigg";
    internal const string BinPowersPiggRelativePath = @"assets\live\bin_powers.pigg";

    public static HomecomingStaticDataSource Discover(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            throw new HomecomingStaticDataSourceException("Homecoming install root is required.");
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(installRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new HomecomingStaticDataSourceException(
                $"Homecoming install root is invalid: {exception.Message}",
                exception);
        }

        if (!Directory.Exists(normalizedRoot))
        {
            throw new HomecomingStaticDataSourceException(
                $"Homecoming install root does not exist: '{normalizedRoot}'.");
        }

        var binPiggPath = RequireFile(normalizedRoot, BinPiggRelativePath, "live bin.pigg");
        var binPowersPiggPath = RequireFile(normalizedRoot, BinPowersPiggRelativePath, "live bin_powers.pigg");
        var livePackagePath = RequireFile(
            normalizedRoot,
            LivePackageMetadataRelativePath,
            "live package metadata");
        var liveDataPackagePath = RequireFile(
            normalizedRoot,
            LiveDataPackageMetadataRelativePath,
            "live data package metadata");

        var buildVersion = ReadRequiredMetadataValue(livePackagePath, "displayver");
        var packageRevision = ReadRequiredMetadataValue(liveDataPackagePath, "version");

        return new HomecomingStaticDataSource
        {
            InstallRoot = normalizedRoot,
            BinPiggPath = binPiggPath,
            BinPowersPiggPath = binPowersPiggPath,
            BuildVersion = buildVersion,
            PackageRevision = packageRevision
        };
    }

    private static string RequireFile(string installRoot, string relativePath, string description)
    {
        var path = Path.GetFullPath(Path.Combine(installRoot, relativePath));
        if (!File.Exists(path))
        {
            throw new HomecomingStaticDataSourceException(
                $"Required {description} was not found: '{path}'.");
        }

        return path;
    }

    private static string ReadRequiredMetadataValue(string metadataPath, string propertyName)
    {
        string text;
        try
        {
            text = File.ReadAllText(metadataPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new HomecomingStaticDataSourceException(
                $"Package metadata '{metadataPath}' could not be read: {exception.Message}",
                exception);
        }

        const string contentStartMarker = "-----BEGIN CONTENT-----";
        const string contentEndMarker = "-----END CONTENT-----";
        var contentStart = text.IndexOf(contentStartMarker, StringComparison.Ordinal);
        if (contentStart < 0)
        {
            throw new HomecomingStaticDataSourceException(
                $"Package metadata '{metadataPath}' is malformed: missing {contentStartMarker} marker.");
        }

        contentStart += contentStartMarker.Length;
        var contentEnd = text.IndexOf(contentEndMarker, contentStart, StringComparison.Ordinal);
        if (contentEnd < 0)
        {
            throw new HomecomingStaticDataSourceException(
                $"Package metadata '{metadataPath}' is malformed: missing {contentEndMarker} marker.");
        }

        var json = text[contentStart..contentEnd].Trim();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !document.RootElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new HomecomingStaticDataSourceException(
                    $"Package metadata '{metadataPath}' is unsupported: required string property '{propertyName}' is missing.");
            }

            return property.GetString()!.Trim();
        }
        catch (JsonException exception)
        {
            throw new HomecomingStaticDataSourceException(
                $"Package metadata '{metadataPath}' contains invalid JSON: {exception.Message}",
                exception);
        }
    }
}

internal sealed class HomecomingStaticDataSourceException : InvalidOperationException
{
    internal HomecomingStaticDataSourceException(string message)
        : base(message)
    {
    }

    internal HomecomingStaticDataSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
