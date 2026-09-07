using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace CoHAnalytics.Services;

public sealed partial class CustomCharacterIconService : ICustomCharacterIconService
{
    private const string FilePrefix = "custom-";
    private readonly SettingsService _settingsService;
    private readonly string _defaultDirectory;

    public CustomCharacterIconService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        var applicationRoot = Path.GetDirectoryName(settingsService.SettingsPath)
            ?? ApplicationDataPaths.GetApplicationRoot();
        _defaultDirectory = ApplicationDataPaths.GetUserCharacterIconsDirectory(applicationRoot);
    }

    internal CustomCharacterIconService(SettingsService settingsService, string defaultDirectory)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _defaultDirectory = Path.GetFullPath(defaultDirectory);
    }

    public IReadOnlyList<CustomCharacterIcon> GetIcons()
    {
        try
        {
            var directory = GetStorageDirectory();
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var icons = new List<CustomCharacterIcon>();
            foreach (var path in Directory.EnumerateFiles(directory, $"{FilePrefix}*.png")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var iconId = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                if (TryLoadValidatedIcon(path, iconId, out var icon))
                {
                    icons.Add(icon);
                }
            }

            return icons;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return [];
        }
    }

    public bool TryGetIcon(string iconId, out CustomCharacterIcon icon)
    {
        icon = null!;
        if (string.IsNullOrWhiteSpace(iconId) || !CustomIconIdPattern().IsMatch(iconId))
        {
            return false;
        }

        try
        {
            var directory = GetStorageDirectory();
            var path = Path.Combine(directory, iconId + ".png");
            return TryLoadValidatedIcon(path, iconId, out icon);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }

    public CustomCharacterIconSaveResult SaveNormalizedIcon(byte[] pngData)
    {
        ArgumentNullException.ThrowIfNull(pngData);
        if (!CustomCharacterIconImageProcessor.TryValidateNormalizedPng(pngData, out var image))
        {
            return CustomCharacterIconSaveResult.Failure(
                "The custom icon could not be saved because its normalized image is invalid.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(pngData)).ToLowerInvariant();
        var iconId = FilePrefix + hash;
        string? tempPath = null;

        try
        {
            var directory = GetStorageDirectory();
            var finalPath = Path.Combine(directory, iconId + ".png");
            tempPath = Path.Combine(directory, $".{iconId}.{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(directory);
            if (!File.Exists(finalPath))
            {
                using (var stream = new FileStream(
                           tempPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 16 * 1024,
                           FileOptions.WriteThrough))
                {
                    stream.Write(pngData);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(tempPath, finalPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(finalPath))
                {
                    File.Delete(tempPath);
                }
            }

            return CustomCharacterIconSaveResult.Success(new CustomCharacterIcon
            {
                Id = iconId,
                ImageSource = image
            });
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            if (tempPath is not null)
            {
                TryDeleteTempFile(tempPath);
            }
            return CustomCharacterIconSaveResult.Failure(
                $"The custom icon could not be written: {exception.Message}");
        }
    }

    public CustomCharacterIconDeleteResult DeleteIcon(string iconId)
    {
        if (string.IsNullOrWhiteSpace(iconId) || !CustomIconIdPattern().IsMatch(iconId))
        {
            return CustomCharacterIconDeleteResult.Failure(
                "The selected custom icon has an invalid app-owned identity.");
        }

        try
        {
            var path = Path.Combine(GetStorageDirectory(), iconId + ".png");
            if (!File.Exists(path))
            {
                return CustomCharacterIconDeleteResult.Success();
            }

            if (!TryLoadValidatedIcon(path, iconId, out _))
            {
                return CustomCharacterIconDeleteResult.Failure(
                    "The selected file could not be verified as an app-owned custom icon.");
            }

            File.Delete(path);
            return CustomCharacterIconDeleteResult.Success();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return CustomCharacterIconDeleteResult.Failure(
                $"The custom icon could not be deleted: {exception.Message}");
        }
    }

    private string GetStorageDirectory()
    {
        var configured = _settingsService.Load().UserCharacterIconsPath;
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : _defaultDirectory;
    }

    private static bool TryLoadValidatedIcon(
        string path,
        string iconId,
        out CustomCharacterIcon icon)
    {
        icon = null!;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            var expectedId = FilePrefix
                + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(iconId, expectedId, StringComparison.Ordinal)
                || !CustomCharacterIconImageProcessor.TryValidateNormalizedPng(bytes, out var image))
            {
                return false;
            }

            icon = new CustomCharacterIcon { Id = iconId, ImageSource = image };
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    [GeneratedRegex("^custom-[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomIconIdPattern();
}

internal sealed class NullCustomCharacterIconService : ICustomCharacterIconService
{
    public static NullCustomCharacterIconService Instance { get; } = new();

    public IReadOnlyList<CustomCharacterIcon> GetIcons() => [];

    public bool TryGetIcon(string iconId, out CustomCharacterIcon icon)
    {
        icon = null!;
        return false;
    }

    public CustomCharacterIconSaveResult SaveNormalizedIcon(byte[] pngData) =>
        CustomCharacterIconSaveResult.Failure("Custom character icons are not available.");

    public CustomCharacterIconDeleteResult DeleteIcon(string iconId) =>
        CustomCharacterIconDeleteResult.Failure("Custom character icons are not available.");
}
