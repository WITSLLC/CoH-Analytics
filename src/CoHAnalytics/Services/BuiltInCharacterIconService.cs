using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoHAnalytics.Services;

public sealed class BuiltInCharacterIconService : IBuiltInCharacterIconService
{
    internal const string BundleResourceName =
        "CoHAnalytics.Assets.Bundles.character-icons.cohicons";

    private readonly IReadOnlyDictionary<string, BuiltInCharacterIcon> _iconsById;

    public BuiltInCharacterIconService()
        : this(() => typeof(BuiltInCharacterIconService).Assembly.GetManifestResourceStream(BundleResourceName))
    {
    }

    internal BuiltInCharacterIconService(Func<Stream?> bundleStreamFactory)
    {
        ArgumentNullException.ThrowIfNull(bundleStreamFactory);

        var icons = TryLoadIcons(bundleStreamFactory);
        Icons = icons;
        _iconsById = icons.ToDictionary(icon => icon.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<BuiltInCharacterIcon> Icons { get; }

    public bool TryGetIcon(string iconId, out BuiltInCharacterIcon icon)
    {
        if (string.IsNullOrWhiteSpace(iconId))
        {
            icon = null!;
            return false;
        }

        return _iconsById.TryGetValue(iconId, out icon!);
    }

    private static IReadOnlyList<BuiltInCharacterIcon> TryLoadIcons(Func<Stream?> bundleStreamFactory)
    {
        try
        {
            using var bundleStream = bundleStreamFactory();
            if (bundleStream is null)
            {
                return [];
            }

            using var archive = new ZipArchive(bundleStream, ZipArchiveMode.Read, leaveOpen: false);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null)
            {
                return [];
            }

            BundleManifest? manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<BundleManifest>(
                    manifestStream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            if (manifest is null
                || manifest.SchemaVersion != 1
                || manifest.Icons.Count != BuiltInCharacterIconIds.Ordered.Count
                || !manifest.Icons.Select(icon => icon.Id).SequenceEqual(BuiltInCharacterIconIds.Ordered))
            {
                return [];
            }

            var icons = new List<BuiltInCharacterIcon>(manifest.Icons.Count);
            foreach (var manifestIcon in manifest.Icons)
            {
                if (string.IsNullOrWhiteSpace(manifestIcon.Entry))
                {
                    return [];
                }

                var iconEntry = archive.GetEntry(manifestIcon.Entry);
                if (iconEntry is null)
                {
                    return [];
                }

                using var iconStream = iconEntry.Open();
                using var cachedStream = new MemoryStream(
                    capacity: checked((int)iconEntry.Length));
                iconStream.CopyTo(cachedStream);
                cachedStream.Position = 0;
                var image = LoadImage(cachedStream);
                icons.Add(new BuiltInCharacterIcon
                {
                    Id = manifestIcon.Id,
                    ImageSource = image
                });
            }

            return icons;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or JsonException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }

    private static ImageSource LoadImage(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.DecodePixelWidth = 256;
        image.StreamSource = stream;
        image.EndInit();
        var cachedImage = new WriteableBitmap(image);
        cachedImage.Freeze();
        return cachedImage;
    }

    private sealed class BundleManifest
    {
        public int SchemaVersion { get; set; }

        public List<BundleManifestIcon> Icons { get; set; } = [];
    }

    private sealed class BundleManifestIcon
    {
        public string Id { get; set; } = string.Empty;

        public string Entry { get; set; } = string.Empty;
    }
}

internal sealed class NullBuiltInCharacterIconService : IBuiltInCharacterIconService
{
    public static NullBuiltInCharacterIconService Instance { get; } = new();

    public IReadOnlyList<BuiltInCharacterIcon> Icons => [];

    public bool TryGetIcon(string iconId, out BuiltInCharacterIcon icon)
    {
        icon = null!;
        return false;
    }
}
