using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;

namespace CoHAnalytics.Tests.Homecoming;

internal static class EnhancementIconCompositionGeometryAssertions
{
    internal static double MeasureFrameColorSurvival(
        BitmapSource leftFrame,
        BitmapSource rightFrame,
        BitmapSource composed)
    {
        var leftPixels = ReadPixels(leftFrame);
        var rightPixels = ReadPixels(rightFrame);
        var composedPixels = ReadPixels(composed);
        var survive = 0;
        var total = 0;

        for (var y = 0; y < EnhancementIconBitmapComposer.FrameHeight; y++)
        {
            for (var x = 0; x < EnhancementIconBitmapComposer.OutputSize; x++)
            {
                var source = x < EnhancementIconBitmapComposer.FrameHalfWidth
                    ? Sample(leftPixels, leftFrame.PixelWidth, x, y)
                    : Sample(rightPixels, rightFrame.PixelWidth, x - EnhancementIconBitmapComposer.FrameHalfWidth, y);
                if (source[3] <= 16)
                {
                    continue;
                }

                total++;
                if (PixelsSimilar(source, Sample(composedPixels, EnhancementIconBitmapComposer.OutputSize, x, y)))
                {
                    survive++;
                }
            }
        }

        return total == 0 ? 0 : (double)survive / total;
    }

    internal static double MeasureCenterColorfulness(BitmapSource composed)
    {
        var pixels = ReadPixels(composed);
        var width = composed.PixelWidth;
        double sum = 0;
        var count = 0;
        for (var y = 20; y < 44; y++)
        {
            for (var x = 20; x < 44; x++)
            {
                var pixel = Sample(pixels, width, x, y);
                if (pixel[3] <= 16)
                {
                    continue;
                }

                sum += pixel[2] + pixel[1] + pixel[0];
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    private static byte[] ReadPixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static byte[] Sample(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return [pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]];
    }

    private static bool PixelsSimilar(byte[] expected, byte[] actual)
    {
        for (var index = 0; index < 3; index++)
        {
            if (Math.Abs(expected[index] - actual[index]) > 8)
            {
                return false;
            }
        }

        return actual[3] > 16;
    }
}
