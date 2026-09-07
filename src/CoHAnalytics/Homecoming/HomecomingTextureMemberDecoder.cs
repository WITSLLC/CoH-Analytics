using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoHAnalytics.Homecoming;

internal readonly record struct HomecomingTextureOverlayPlacement(int OffsetX, int OffsetY);

internal static class HomecomingTextureMemberDecoder
{
    private const int DdsHeaderSize = 128;
    private const int WrapperLogicalWidthOffset = 8;
    private const int WrapperLogicalHeightOffset = 12;
    private const int MinimumWrapperMetadataLength = 16;
    private const uint DdsMagic = 0x20534444;
    private const uint DdpfAlphabitmaps = 0x00000001;
    private const uint DdpfRgb = 0x00000040;

    internal static bool TryReadOverlayPlacement(
        byte[] textureMemberBytes,
        out HomecomingTextureOverlayPlacement placement)
    {
        placement = default;
        if (!TryReadDdsDimensions(textureMemberBytes, out var ddsOffset, out var width, out var height))
        {
            return false;
        }

        if (ddsOffset < MinimumWrapperMetadataLength)
        {
            placement = new HomecomingTextureOverlayPlacement(0, 0);
            return true;
        }

        var logicalWidth = BinaryPrimitives.ReadInt32LittleEndian(
            textureMemberBytes.AsSpan(WrapperLogicalWidthOffset, 4));
        var logicalHeight = BinaryPrimitives.ReadInt32LittleEndian(
            textureMemberBytes.AsSpan(WrapperLogicalHeightOffset, 4));
        if (logicalWidth <= 0
            || logicalHeight <= 0
            || logicalWidth > width
            || logicalHeight > height)
        {
            placement = new HomecomingTextureOverlayPlacement(0, 0);
            return true;
        }

        placement = new HomecomingTextureOverlayPlacement(
            (width - logicalWidth) / 2,
            (height - logicalHeight) / 2);
        return true;
    }

    internal static BitmapSource? TryDecodeTextureMember(byte[] textureMemberBytes)
    {
        if (textureMemberBytes.Length < DdsHeaderSize)
        {
            return null;
        }

        if (!TryReadDdsDimensions(textureMemberBytes, out var ddsOffset, out var width, out var height))
        {
            return null;
        }

        var header = textureMemberBytes.AsSpan(ddsOffset, DdsHeaderSize);
        var pixelFormatFlags = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(80, 4));
        var rgbBitCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(88, 4));
        if ((pixelFormatFlags & DdpfRgb) == 0 || rgbBitCount != 32)
        {
            return null;
        }

        var pixelOffset = checked(ddsOffset + DdsHeaderSize);
        var pixelLength = checked(width * height * 4);
        if (pixelOffset + pixelLength > textureMemberBytes.Length)
        {
            return null;
        }

        var pixels = new byte[pixelLength];
        Buffer.BlockCopy(textureMemberBytes, pixelOffset, pixels, 0, pixelLength);

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

    internal static bool HasAlphaChannel(byte[] textureMemberBytes)
    {
        var ddsOffset = FindDdsOffset(textureMemberBytes);
        if (ddsOffset < 0 || checked(ddsOffset + DdsHeaderSize) > textureMemberBytes.Length)
        {
            return false;
        }

        var header = textureMemberBytes.AsSpan(ddsOffset, DdsHeaderSize);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != DdsMagic)
        {
            return false;
        }

        var pixelFormatFlags = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(80, 4));
        return (pixelFormatFlags & (DdpfRgb | DdpfAlphabitmaps)) == (DdpfRgb | DdpfAlphabitmaps);
    }

    private static bool TryReadDdsDimensions(
        byte[] textureMemberBytes,
        out int ddsOffset,
        out int width,
        out int height)
    {
        ddsOffset = FindDdsOffset(textureMemberBytes);
        width = 0;
        height = 0;
        if (ddsOffset < 0 || checked(ddsOffset + DdsHeaderSize) > textureMemberBytes.Length)
        {
            return false;
        }

        var header = textureMemberBytes.AsSpan(ddsOffset, DdsHeaderSize);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != DdsMagic)
        {
            return false;
        }

        height = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(12, 4));
        width = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(16, 4));
        return width > 0 && height > 0;
    }

    private static int FindDdsOffset(byte[] textureMemberBytes)
    {
        var limit = textureMemberBytes.Length - 4;
        for (var index = 0; index <= limit; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(textureMemberBytes.AsSpan(index, 4)) == DdsMagic)
            {
                return index;
            }
        }

        return -1;
    }
}
