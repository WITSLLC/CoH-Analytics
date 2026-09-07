using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoHAnalytics.Homecoming;

internal static class EnhancementIconBitmapComposer
{
    internal const int OutputSize = 64;
    internal const int FrameHalfWidth = 32;
    internal const int FrameHeight = 64;

    internal static BitmapSource? TryCompose(
        BitmapSource? leftFrame,
        BitmapSource? rightFrame,
        BitmapSource? pog,
        BitmapSource? innerArt,
        HomecomingTextureOverlayPlacement pogPlacement = default,
        HomecomingTextureOverlayPlacement innerPlacement = default)
    {
        if (leftFrame is null || rightFrame is null || pog is null || innerArt is null)
        {
            return null;
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(
                pog,
                new Rect(
                    pogPlacement.OffsetX,
                    pogPlacement.OffsetY,
                    pog.PixelWidth,
                    pog.PixelHeight));
            context.DrawImage(
                innerArt,
                new Rect(
                    innerPlacement.OffsetX,
                    innerPlacement.OffsetY,
                    innerArt.PixelWidth,
                    innerArt.PixelHeight));
            context.DrawImage(leftFrame, new Rect(0, 0, FrameHalfWidth, FrameHeight));
            context.DrawImage(rightFrame, new Rect(FrameHalfWidth, 0, FrameHalfWidth, FrameHeight));
        }

        var target = new RenderTargetBitmap(
            OutputSize,
            OutputSize,
            96,
            96,
            PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
