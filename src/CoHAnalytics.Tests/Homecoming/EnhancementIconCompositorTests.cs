using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class EnhancementIconCompositorTests
{
    [Fact]
    public void Compose_WithSyntheticLayers_Produces64x64FrozenBitmap()
    {
        var compositor = CreateSyntheticCompositor();

        var composed = compositor.TryCompose(new EnhancementIconCompositionRequest
        {
            IconIdentity = "inner.tga",
            PogBoostType = "Damage",
            FrameClass = EnhancementFrameClass.Rare
        });

        Assert.NotNull(composed);
        Assert.Equal(64, composed!.Width);
        Assert.Equal(64, composed.Height);
        Assert.True(composed.IsFrozen);
        Assert.Equal(PixelFormats.Pbgra32, ((BitmapSource)composed).Format);
    }

    [Fact]
    public void Compose_FrameHalves_DrawAtLeftAndRightPositions()
    {
        var provider = new FakeAssetProvider()
            .With("e_orgin_rare_l.tga", CreateSolidBitmap(32, 64, 255, 0, 0, 255))
            .With("e_orgin_rare_r.tga", CreateSolidBitmap(32, 64, 0, 255, 0, 255))
            .With("E_POG_DAMAGE.tga", CreateTransparentBitmap(64, 64))
            .With("inner.tga", CreateTransparentBitmap(64, 64));

        var compositor = new EnhancementIconCompositor(provider);
        var composed = (BitmapSource)compositor.TryCompose(new EnhancementIconCompositionRequest
        {
            IconIdentity = "inner.tga",
            PogBoostType = "Damage",
            FrameClass = EnhancementFrameClass.Rare
        })!;

        AssertPixel(composed, 8, 32, 255, 0, 0, 255);
        AssertPixel(composed, 40, 32, 0, 255, 0, 255);
    }

    [Fact]
    public void Compose_Frame_DrawsAbovePogAndInnerArt()
    {
        var provider = new FakeAssetProvider()
            .With("e_orgin_rare_l.tga", CreateEdgeMarkerBitmap(32, 64, 255, 0, 0, 255, markerX: 0))
            .With("e_orgin_rare_r.tga", CreateTransparentBitmap(32, 64))
            .With("E_POG_DAMAGE.tga", CreateSolidBitmap(64, 64, 0, 0, 255, 255))
            .With("inner.tga", CreateSolidBitmap(64, 64, 255, 255, 0, 255));

        var compositor = new EnhancementIconCompositor(provider);
        var composed = (BitmapSource)compositor.TryCompose(new EnhancementIconCompositionRequest
        {
            IconIdentity = "inner.tga",
            PogBoostType = "Damage",
            FrameClass = EnhancementFrameClass.Rare
        })!;

        AssertPixel(composed, 0, 32, 255, 0, 0, 255);
        AssertPixel(composed, 32, 32, 255, 255, 0, 255);
    }

    [Fact]
    public void Compose_OverlayPlacement_OffsetsPogAndInnerArt()
    {
        var pog = CreateCenterPixelBitmap(64, 64, 0, 0, 255, 255);
        var inner = CreateCenterPixelBitmap(64, 64, 255, 255, 255, 255);
        var composed = EnhancementIconBitmapComposer.TryCompose(
            CreateTransparentBitmap(32, 64),
            CreateTransparentBitmap(32, 64),
            pog,
            inner,
            new HomecomingTextureOverlayPlacement(8, 8),
            new HomecomingTextureOverlayPlacement(9, 9));

        Assert.NotNull(composed);
        AssertPixel(composed!, 41, 41, 255, 255, 255, 255);
        AssertPixel(composed!, 40, 40, 0, 0, 255, 255);
    }

    [Fact]
    public void Compose_InnerArt_DrawsAbovePog()
    {
        var provider = new FakeAssetProvider()
            .With("e_orgin_rare_l.tga", CreateTransparentBitmap(32, 64))
            .With("e_orgin_rare_r.tga", CreateTransparentBitmap(32, 64))
            .With("E_POG_DAMAGE.tga", CreateSolidBitmap(64, 64, 0, 0, 255, 255))
            .With("inner.tga", CreateCenterPixelBitmap(64, 64, 255, 255, 255, 255));

        var compositor = new EnhancementIconCompositor(provider);
        var composed = (BitmapSource)compositor.TryCompose(new EnhancementIconCompositionRequest
        {
            IconIdentity = "inner.tga",
            PogBoostType = "Damage",
            FrameClass = EnhancementFrameClass.Rare
        })!;

        AssertPixel(composed, 32, 32, 255, 255, 255, 255);
        AssertPixel(composed, 4, 4, 0, 0, 255, 255);
    }

    [Fact]
    public void Compose_PreservesAlphaChannel()
    {
        var provider = new FakeAssetProvider()
            .With("e_orgin_rare_l.tga", CreateTransparentBitmap(32, 64))
            .With("e_orgin_rare_r.tga", CreateTransparentBitmap(32, 64))
            .With("E_POG_DAMAGE.tga", CreateTransparentBitmap(64, 64))
            .With("inner.tga", CreateTransparentBitmap(64, 64));

        var compositor = new EnhancementIconCompositor(provider);
        var composed = (BitmapSource)compositor.TryCompose(new EnhancementIconCompositionRequest
        {
            IconIdentity = "inner.tga",
            PogBoostType = "Damage",
            FrameClass = EnhancementFrameClass.Rare
        })!;

        AssertPixel(composed, 10, 10, 0, 0, 0, 0);
    }

    [Fact]
    public void Compose_RepeatedRequest_ReturnsCachedInstance()
    {
        var compositor = CreateSyntheticCompositor();
        var request = new EnhancementIconCompositionRequest
        {
            IconIdentity = "inner.tga",
            PogBoostType = "Damage",
            FrameClass = EnhancementFrameClass.Rare
        };

        var first = compositor.TryCompose(request);
        var second = compositor.TryCompose(request);

        Assert.Same(first, second);
    }

    [Fact]
    public void Compose_MissingLayer_ReturnsNull()
    {
        var provider = new FakeAssetProvider()
            .With("e_orgin_rare_l.tga", CreateSolidBitmap(32, 64, 255, 0, 0, 255));

        var compositor = new EnhancementIconCompositor(provider);
        var composed = compositor.TryCompose(new EnhancementIconCompositionRequest
        {
            IconIdentity = "E_ICON_MISSING.tga",
            PogBoostType = "Damage",
            FrameClass = EnhancementFrameClass.Rare
        });

        Assert.Null(composed);
    }

    [Fact]
    public void Compose_UnknownPogMapping_ReturnsNull()
    {
        var provider = new FakeAssetProvider()
            .With("e_orgin_rare_l.tga", CreateSolidBitmap(32, 64, 255, 0, 0, 255))
            .With("e_orgin_rare_r.tga", CreateSolidBitmap(32, 64, 0, 255, 0, 255))
            .With("inner.tga", CreateSolidBitmap(64, 64, 255, 255, 0, 255));

        var compositor = new EnhancementIconCompositor(provider);
        var composed = compositor.TryCompose(new EnhancementIconCompositionRequest
        {
            IconIdentity = "inner.tga",
            PogBoostType = "Hamidon",
            FrameClass = EnhancementFrameClass.Rare
        });

        Assert.Null(composed);
    }

    private static EnhancementIconCompositor CreateSyntheticCompositor()
    {
        var provider = new FakeAssetProvider()
            .With("e_orgin_rare_l.tga", CreateSolidBitmap(32, 64, 255, 0, 0, 255))
            .With("e_orgin_rare_r.tga", CreateSolidBitmap(32, 64, 0, 255, 0, 255))
            .With("E_POG_DAMAGE.tga", CreateSolidBitmap(64, 64, 0, 0, 255, 255))
            .With("inner.tga", CreateSolidBitmap(64, 64, 255, 255, 0, 255));

        return new EnhancementIconCompositor(provider);
    }

    private static BitmapSource CreateEdgeMarkerBitmap(
        int width,
        int height,
        byte b,
        byte g,
        byte r,
        byte a,
        int markerX)
    {
        var pixels = new byte[width * height * 4];
        var offset = ((height / 2 * width) + markerX) * 4;
        pixels[offset] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
        pixels[offset + 3] = a;

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateSolidBitmap(int width, int height, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = a;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateTransparentBitmap(int width, int height) =>
        CreateSolidBitmap(width, height, 0, 0, 0, 0);

    private static BitmapSource CreateCenterPixelBitmap(int width, int height, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[width * height * 4];
        var centerOffset = ((height / 2 * width) + (width / 2)) * 4;
        pixels[centerOffset] = b;
        pixels[centerOffset + 1] = g;
        pixels[centerOffset + 2] = r;
        pixels[centerOffset + 3] = a;

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AssertPixel(BitmapSource bitmap, int x, int y, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixels, 4, 0);
        Assert.Equal(b, pixels[0]);
        Assert.Equal(g, pixels[1]);
        Assert.Equal(r, pixels[2]);
        Assert.Equal(a, pixels[3]);
    }

    private sealed class FakeAssetProvider : IInstalledGameAssetProvider
    {
        private readonly Dictionary<string, ImageSource> _assets = new(StringComparer.OrdinalIgnoreCase);

        public FakeAssetProvider With(string identity, ImageSource imageSource)
        {
            _assets[identity] = imageSource;
            return this;
        }

        public ImageSource? TryResolve(string? iconIdentity)
        {
            if (string.IsNullOrWhiteSpace(iconIdentity))
            {
                return null;
            }

            return _assets.TryGetValue(iconIdentity.Trim(), out var imageSource)
                ? imageSource
                : null;
        }
    }
}
