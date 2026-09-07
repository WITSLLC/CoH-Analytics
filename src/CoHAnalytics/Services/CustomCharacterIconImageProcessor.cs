using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoHAnalytics.Services;

public sealed record CustomCharacterIconSource
{
    public required BitmapSource Image { get; init; }

    public required int OriginalPixelWidth { get; init; }

    public required int OriginalPixelHeight { get; init; }
}

public sealed record CustomCharacterIconLoadResult
{
    public bool IsSuccess { get; init; }

    public CustomCharacterIconSource? Source { get; init; }

    public string? ErrorMessage { get; init; }
}

public static class CustomCharacterIconImageProcessor
{
    public const int OutputSize = 256;
    public const long MaximumSourceBytes = 25L * 1024 * 1024;
    public const int MaximumSourceDimension = 16_384;
    public const long MaximumSourcePixels = 64_000_000;
    public const int MaximumWorkingDimension = 4096;

    public static CustomCharacterIconLoadResult LoadSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Failure("Select a PNG or JPEG image.");
        }

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return Failure("The selected image no longer exists.");
            }

            if (file.Length <= 0 || file.Length > MaximumSourceBytes)
            {
                return Failure("The selected image must be no larger than 25 MB.");
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);
            if (decoder is not PngBitmapDecoder and not JpegBitmapDecoder
                || decoder.Frames.Count != 1)
            {
                return Failure("Only single-image PNG and JPEG files are supported.");
            }

            var frame = decoder.Frames[0];
            var width = frame.PixelWidth;
            var height = frame.PixelHeight;
            if (width <= 0
                || height <= 0
                || width > MaximumSourceDimension
                || height > MaximumSourceDimension
                || (long)width * height > MaximumSourcePixels)
            {
                return Failure("The selected image dimensions are too large to import safely.");
            }

            var longestSide = Math.Max(width, height);
            stream.Position = 0;
            var working = new BitmapImage();
            working.BeginInit();
            working.CacheOption = BitmapCacheOption.OnLoad;
            working.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            if (longestSide > MaximumWorkingDimension)
            {
                if (width >= height)
                {
                    working.DecodePixelWidth = MaximumWorkingDimension;
                }
                else
                {
                    working.DecodePixelHeight = MaximumWorkingDimension;
                }
            }

            working.StreamSource = stream;
            working.EndInit();
            working.Freeze();

            return new CustomCharacterIconLoadResult
            {
                IsSuccess = true,
                Source = new CustomCharacterIconSource
                {
                    Image = working,
                    OriginalPixelWidth = width,
                    OriginalPixelHeight = height
                }
            };
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return Failure("The selected file is not a valid supported image.");
        }
    }

    public static byte[] RenderNormalizedPng(
        BitmapSource source,
        double scale,
        double offsetX,
        double offsetY,
        double editorViewportSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            throw new ArgumentException("The source image has invalid dimensions.", nameof(source));
        }

        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        if (!double.IsFinite(editorViewportSize) || editorViewportSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(editorViewportSize));
        }

        var coverScale = Math.Max(
            OutputSize / (double)source.PixelWidth,
            OutputSize / (double)source.PixelHeight);
        var renderScale = coverScale * scale;
        var destinationWidth = source.PixelWidth * renderScale;
        var destinationHeight = source.PixelHeight * renderScale;
        var editorToOutput = OutputSize / editorViewportSize;
        var destinationX = (OutputSize - destinationWidth) / 2 + offsetX * editorToOutput;
        var destinationY = (OutputSize - destinationHeight) / 2 + offsetY * editorToOutput;

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(
                source,
                new Rect(destinationX, destinationY, destinationWidth, destinationHeight));
        }

        var bitmap = new RenderTargetBitmap(
            OutputSize,
            OutputSize,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    internal static bool TryValidateNormalizedPng(byte[] data, out BitmapSource image)
    {
        image = null!;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1
                || decoder.Frames[0].PixelWidth != OutputSize
                || decoder.Frames[0].PixelHeight != OutputSize)
            {
                return false;
            }

            var cached = new WriteableBitmap(decoder.Frames[0]);
            cached.Freeze();
            image = cached;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static CustomCharacterIconLoadResult Failure(string message) =>
        new() { ErrorMessage = message };
}
