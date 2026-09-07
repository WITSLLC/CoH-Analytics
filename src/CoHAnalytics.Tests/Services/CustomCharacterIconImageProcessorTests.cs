using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

[Collection(WpfDispatcherCollection.Name)]
public sealed class CustomCharacterIconImageProcessorTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public CustomCharacterIconImageProcessorTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Normalization_creates_a_256_square_png_without_the_composition_guide() =>
        _dispatcher.InvokeAsync(() =>
        {
            var source = CreateSolidBitmap(40, 20, red: 180, green: 30, blue: 140, alpha: 255);

            var png = CustomCharacterIconImageProcessor.RenderNormalizedPng(
                source,
                scale: 1,
                offsetX: 0,
                offsetY: 0,
                editorViewportSize: 420);

            var decoded = DecodePng(png);
            Assert.Equal(256, decoded.PixelWidth);
            Assert.Equal(256, decoded.PixelHeight);
            Assert.Equal((180, 30, 140, 255), ReadPixel(decoded, 0, 0));
            Assert.Equal((180, 30, 140, 255), ReadPixel(decoded, 128, 128));
        });

    [Fact]
    public Task Normalization_preserves_transparent_and_opaque_source_pixels() =>
        _dispatcher.InvokeAsync(() =>
        {
            var transparentSource = CreateSolidBitmap(16, 16, 20, 40, 60, 64);
            var transparentPng = CustomCharacterIconImageProcessor.RenderNormalizedPng(
                transparentSource, 1, 0, 0, 420);
            var transparentPixel = ReadPixel(DecodePng(transparentPng), 128, 128);
            Assert.InRange(transparentPixel.A, 62, 66);
            Assert.InRange(transparentPixel.R, 18, 22);
            Assert.InRange(transparentPixel.G, 38, 42);
            Assert.InRange(transparentPixel.B, 58, 62);

            var opaqueSource = CreateSolidBitmap(16, 16, 90, 80, 70, 255);
            var opaquePng = CustomCharacterIconImageProcessor.RenderNormalizedPng(
                opaqueSource, 1, 0, 0, 420);
            Assert.Equal((90, 80, 70, 255), ReadPixel(DecodePng(opaquePng), 128, 128));
        });

    [Fact]
    public Task Normalization_allows_shrinking_below_cover_fit_and_leaves_transparent_space() =>
        _dispatcher.InvokeAsync(() =>
        {
            var source = CreateSolidBitmap(16, 16, 90, 80, 70, 255);

            var png = CustomCharacterIconImageProcessor.RenderNormalizedPng(
                source, 0.5, 0, 0, 420);
            var decoded = DecodePng(png);

            Assert.Equal((0, 0, 0, 0), ReadPixel(decoded, 0, 0));
            Assert.Equal((90, 80, 70, 255), ReadPixel(decoded, 128, 128));
        });

    [Fact]
    public Task Source_is_independent_of_the_original_file_after_safe_decode() =>
        _dispatcher.InvokeAsync(() =>
        {
            var directory = CreateDirectory();
            var sourcePath = Path.Combine(directory, "portrait.png");
            File.WriteAllBytes(sourcePath, EncodePng(CreateSolidBitmap(30, 50, 40, 100, 180, 255)));

            var result = CustomCharacterIconImageProcessor.LoadSource(sourcePath);
            Assert.True(result.IsSuccess, result.ErrorMessage);
            File.Delete(sourcePath);

            var normalized = CustomCharacterIconImageProcessor.RenderNormalizedPng(
                result.Source!.Image, 1, 0, 0, 420);
            Assert.True(CustomCharacterIconImageProcessor.TryValidateNormalizedPng(normalized, out _));
        });

    [Fact]
    public Task Malformed_and_absurd_dimension_sources_are_rejected() =>
        _dispatcher.InvokeAsync(() =>
        {
            var directory = CreateDirectory();
            var malformed = Path.Combine(directory, "broken.png");
            File.WriteAllText(malformed, "not an image");
            Assert.False(CustomCharacterIconImageProcessor.LoadSource(malformed).IsSuccess);

            var oversized = Path.Combine(directory, "oversized.png");
            File.WriteAllBytes(
                oversized,
                EncodePng(CreateSolidBitmap(
                    CustomCharacterIconImageProcessor.MaximumSourceDimension + 1,
                    1,
                    1,
                    2,
                    3,
                    255)));
            var result = CustomCharacterIconImageProcessor.LoadSource(oversized);
            Assert.False(result.IsSuccess);
            Assert.Contains("dimensions", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        });

    internal static BitmapSource CreateSolidBitmap(
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = alpha;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    internal static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource DecodePng(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static (byte R, byte G, byte B, byte A) ReadPixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-custom-icon-processor",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
