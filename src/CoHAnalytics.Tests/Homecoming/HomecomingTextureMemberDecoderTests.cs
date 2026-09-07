using System.Buffers.Binary;
using System.Windows.Media;
using CoHAnalytics.Homecoming;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class HomecomingTextureMemberDecoderTests
{
    [Fact]
    public void TryReadOverlayPlacement_CentersLogicalContentWithinCanvas()
    {
        var textureMember = HomecomingTextureFixtureBuilder.CreateWrappedBgraTexture(
            width: 64,
            height: 64,
            logicalWidth: 48,
            logicalHeight: 48,
            includeAlpha: true);

        Assert.True(HomecomingTextureMemberDecoder.TryReadOverlayPlacement(textureMember, out var placement));
        Assert.Equal(8, placement.OffsetX);
        Assert.Equal(8, placement.OffsetY);
    }

    [Fact]
    public void TryReadOverlayPlacement_FrameSizedAsset_HasZeroOffset()
    {
        var textureMember = HomecomingTextureFixtureBuilder.CreateWrappedBgraTexture(
            width: 32,
            height: 64,
            logicalWidth: 32,
            logicalHeight: 64,
            includeAlpha: true);

        Assert.True(HomecomingTextureMemberDecoder.TryReadOverlayPlacement(textureMember, out var placement));
        Assert.Equal(0, placement.OffsetX);
        Assert.Equal(0, placement.OffsetY);
    }

    [Fact]
    public void TryDecodeTextureMember_DecodesEmbeddedBgraDds()
    {
        var textureMember = HomecomingTextureFixtureBuilder.CreateWrappedBgraTexture(width: 4, height: 4, includeAlpha: true);

        var bitmap = HomecomingTextureMemberDecoder.TryDecodeTextureMember(textureMember);

        Assert.NotNull(bitmap);
        Assert.Equal(4, bitmap!.PixelWidth);
        Assert.Equal(4, bitmap.PixelHeight);
        Assert.Equal(PixelFormats.Bgra32, bitmap.Format);
        Assert.True(bitmap.IsFrozen);
        Assert.True(HomecomingTextureMemberDecoder.HasAlphaChannel(textureMember));
    }

    [Fact]
    public void TryDecodeTextureMember_UnsupportedPayload_ReturnsNull()
    {
        var bitmap = HomecomingTextureMemberDecoder.TryDecodeTextureMember([1, 2, 3, 4]);

        Assert.Null(bitmap);
    }
}

internal static class HomecomingTextureFixtureBuilder
{
    internal static byte[] CreateWrappedBgraTexture(
        int width,
        int height,
        bool includeAlpha,
        int? logicalWidth = null,
        int? logicalHeight = null)
    {
        var ddsHeader = CreateDdsHeader(width, height, includeAlpha);
        var pixels = CreateSolidBgraPixels(width, height, blue: 40, green: 80, red: 120, alpha: includeAlpha ? (byte)200 : byte.MaxValue);

        var resolvedLogicalWidth = logicalWidth ?? width;
        var resolvedLogicalHeight = logicalHeight ?? height;
        var wrapperPrefix = new byte[98];
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix.AsSpan(0, 4), wrapperPrefix.Length);
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix.AsSpan(8, 4), resolvedLogicalWidth);
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix.AsSpan(12, 4), resolvedLogicalHeight);

        var payload = new byte[wrapperPrefix.Length + ddsHeader.Length + pixels.Length];
        Buffer.BlockCopy(wrapperPrefix, 0, payload, 0, wrapperPrefix.Length);
        Buffer.BlockCopy(ddsHeader, 0, payload, wrapperPrefix.Length, ddsHeader.Length);
        Buffer.BlockCopy(pixels, 0, payload, wrapperPrefix.Length + ddsHeader.Length, pixels.Length);
        return payload;
    }

    private static byte[] CreateDdsHeader(int width, int height, bool includeAlpha)
    {
        var header = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 0x00001007);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), height);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76, 4), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80, 4), includeAlpha ? 0x00000041u : 0x00000040u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(88, 4), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(92, 4), 0x00ff0000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(96, 4), 0x0000ff00);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(100, 4), 0x000000ff);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(104, 4), 0xff000000);
        return header;
    }

    private static byte[] CreateSolidBgraPixels(int width, int height, byte blue, byte green, byte red, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = alpha;
        }

        return pixels;
    }
}
