using System.Buffers.Binary;
using System.Numerics;

namespace HoI4ModBuilder.Game.Imaging;

/// <summary>
/// Decodes the top mip level of DirectDraw Surface textures. Covers everything Hearts of Iron IV
/// ships: uncompressed formats of any bit layout (via channel masks), DXT1/3/5 (BC1–3),
/// the RXGB normal-map variant and the plain DX10 8-bit formats.
/// </summary>
public static class DdsDecoder
{
    private const int HeaderSize = 128; // magic + DDS_HEADER
    private const int Dx10HeaderSize = 20;

    // DDS_PIXELFORMAT flags
    private const uint AlphaPixels = 0x1;
    private const uint AlphaOnly = 0x2;
    private const uint FourCc = 0x4;
    private const uint Luminance = 0x20000;

    public static bool IsDds(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual("DDS "u8);

    /// <exception cref="InvalidDataException">The data is not a supported DDS texture.</exception>
    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (!IsDds(data) || data.Length < HeaderSize)
            throw new InvalidDataException("Не DDS-файл.");

        var height = ReadInt(data, 12);
        var width = ReadInt(data, 16);
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new InvalidDataException($"Неприпустимий розмір DDS: {width}×{height}.");

        var flags = ReadUInt(data, 80);
        var fourCc = System.Text.Encoding.ASCII.GetString(data.Slice(84, 4));

        if ((flags & FourCc) == 0)
            return DecodeUncompressed(data, width, height, flags);

        return fourCc switch
        {
            "DXT1" => DecodeBlocks(data[HeaderSize..], width, height, BlockFormat.Bc1, "DXT1"),
            "DXT2" or "DXT3" => DecodeBlocks(data[HeaderSize..], width, height, BlockFormat.Bc2, fourCc),
            "DXT4" or "DXT5" => DecodeBlocks(data[HeaderSize..], width, height, BlockFormat.Bc3, fourCc),
            "RXGB" => SwizzleRxgb(DecodeBlocks(data[HeaderSize..], width, height, BlockFormat.Bc3, "RXGB")),
            "DX10" => DecodeDx10(data, width, height),
            _ => throw new InvalidDataException($"Формат DDS «{fourCc.TrimEnd('\0')}» не підтримується."),
        };
    }

    private static DecodedImage DecodeDx10(ReadOnlySpan<byte> data, int width, int height)
    {
        if (data.Length < HeaderSize + Dx10HeaderSize)
            throw new InvalidDataException("Пошкоджений заголовок DX10.");
        var dxgi = ReadUInt(data, HeaderSize);
        var pixels = data[(HeaderSize + Dx10HeaderSize)..];
        return dxgi switch
        {
            28 or 29 => DecodeMasked(pixels, width, height, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000, "RGBA8"),
            87 or 91 => DecodeMasked(pixels, width, height, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000, "BGRA8"),
            88 or 93 => DecodeMasked(pixels, width, height, 32, 0xFF0000, 0xFF00, 0xFF, 0, "BGRX8"),
            71 or 72 => DecodeBlocks(pixels, width, height, BlockFormat.Bc1, "BC1"),
            74 or 75 => DecodeBlocks(pixels, width, height, BlockFormat.Bc2, "BC2"),
            77 or 78 => DecodeBlocks(pixels, width, height, BlockFormat.Bc3, "BC3"),
            _ => throw new InvalidDataException($"Формат DXGI {dxgi} не підтримується."),
        };
    }

    /// <summary>Any uncompressed layout, described by the per-channel bit masks in the header.</summary>
    private static DecodedImage DecodeUncompressed(ReadOnlySpan<byte> data, int width, int height, uint flags)
    {
        var bits = (int)ReadUInt(data, 88);
        uint r = ReadUInt(data, 92), g = ReadUInt(data, 96), b = ReadUInt(data, 100), a = ReadUInt(data, 104);

        if ((flags & (AlphaPixels | AlphaOnly)) == 0)
            a = 0; // e.g. X1R5G5B5: the spare bit is not alpha
        if ((flags & Luminance) != 0)
            g = b = r;

        var format = (flags & AlphaOnly) != 0 ? $"A{bits}"
            : (flags & Luminance) != 0 ? $"L{bits}"
            : a != 0 ? $"RGBA {bits}-bit"
            : $"RGB {bits}-bit";
        // Alpha-only textures are masks: show them as white with the mask as transparency.
        var colorFallback = (flags & AlphaOnly) != 0 ? (byte)255 : (byte)0;
        return DecodeMasked(data[HeaderSize..], width, height, bits, r, g, b, a, format, colorFallback);
    }

    private static DecodedImage DecodeMasked(
        ReadOnlySpan<byte> source, int width, int height, int bits,
        uint rMask, uint gMask, uint bMask, uint aMask, string format, byte colorFallback = 0)
    {
        if (bits is not (8 or 16 or 24 or 32))
            throw new InvalidDataException($"Глибина кольору {bits} біт не підтримується.");
        var bytesPerPixel = bits / 8;
        var pitch = width * bytesPerPixel;
        if (source.Length < pitch * height)
            throw new InvalidDataException("DDS-файл обрізаний.");

        var r = new Channel(rMask);
        var g = new Channel(gMask);
        var b = new Channel(bMask);
        var a = new Channel(aMask);
        var output = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var row = source.Slice(y * pitch, pitch);
            for (var x = 0; x < width; x++)
            {
                var p = row.Slice(x * bytesPerPixel, bytesPerPixel);
                uint value = bytesPerPixel switch
                {
                    1 => p[0],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(p),
                    3 => (uint)(p[0] | p[1] << 8 | p[2] << 16),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(p),
                };
                var o = (y * width + x) * 4;
                output[o] = b.Extract(value, colorFallback);
                output[o + 1] = g.Extract(value, colorFallback);
                output[o + 2] = r.Extract(value, colorFallback);
                output[o + 3] = a.Extract(value, 255);
            }
        }
        return new DecodedImage(width, height, output, format);
    }

    private enum BlockFormat
    {
        Bc1,
        Bc2,
        Bc3,
    }

    private static DecodedImage DecodeBlocks(ReadOnlySpan<byte> source, int width, int height, BlockFormat format, string name)
    {
        var blockSize = format == BlockFormat.Bc1 ? 8 : 16;
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        if (source.Length < blocksX * blocksY * blockSize)
            throw new InvalidDataException("DDS-файл обрізаний.");

        var output = new byte[width * height * 4];
        Span<byte> block = stackalloc byte[16 * 4]; // 4×4 BGRA
        Span<byte> alpha = stackalloc byte[16];

        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var data = source.Slice((by * blocksX + bx) * blockSize, blockSize);
                switch (format)
                {
                    case BlockFormat.Bc1:
                        DecodeColorBlock(data, block, allowTransparency: true);
                        break;
                    case BlockFormat.Bc2:
                        DecodeColorBlock(data[8..], block, allowTransparency: false);
                        DecodeExplicitAlpha(data[..8], alpha);
                        ApplyAlpha(block, alpha);
                        break;
                    default:
                        DecodeColorBlock(data[8..], block, allowTransparency: false);
                        DecodeInterpolatedAlpha(data[..8], alpha);
                        ApplyAlpha(block, alpha);
                        break;
                }
                CopyBlock(block, output, width, height, bx * 4, by * 4);
            }
        }
        return new DecodedImage(width, height, output, name);
    }

    /// <summary>BC1 colour block: two RGB565 endpoints and 2-bit indices.</summary>
    private static void DecodeColorBlock(ReadOnlySpan<byte> data, Span<byte> block, bool allowTransparency)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var indices = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);

        Span<byte> palette = stackalloc byte[16]; // 4 × BGRA
        Expand565(c0, palette[..4]);
        Expand565(c1, palette[4..8]);

        if (c0 > c1 || !allowTransparency)
        {
            for (var i = 0; i < 3; i++)
            {
                palette[8 + i] = (byte)((2 * palette[i] + palette[4 + i] + 1) / 3);
                palette[12 + i] = (byte)((palette[i] + 2 * palette[4 + i] + 1) / 3);
            }
            palette[11] = palette[15] = 255;
        }
        else
        {
            for (var i = 0; i < 3; i++)
                palette[8 + i] = (byte)((palette[i] + palette[4 + i]) / 2);
            palette[11] = 255;
            palette[12] = palette[13] = palette[14] = palette[15] = 0; // transparent black
        }

        for (var i = 0; i < 16; i++)
        {
            var index = (int)((indices >> (2 * i)) & 3);
            palette.Slice(index * 4, 4).CopyTo(block.Slice(i * 4, 4));
        }
    }

    /// <summary>BC2: sixteen explicit 4-bit alpha values.</summary>
    private static void DecodeExplicitAlpha(ReadOnlySpan<byte> data, Span<byte> alpha)
    {
        var bits = BinaryPrimitives.ReadUInt64LittleEndian(data);
        for (var i = 0; i < 16; i++)
            alpha[i] = (byte)(((bits >> (4 * i)) & 0xF) * 17);
    }

    /// <summary>BC3: two alpha endpoints and 3-bit indices into an 8-entry ramp.</summary>
    private static void DecodeInterpolatedAlpha(ReadOnlySpan<byte> data, Span<byte> alpha)
    {
        int a0 = data[0], a1 = data[1];
        Span<byte> ramp = stackalloc byte[8];
        ramp[0] = (byte)a0;
        ramp[1] = (byte)a1;
        if (a0 > a1)
        {
            for (var i = 1; i < 7; i++)
                ramp[i + 1] = (byte)(((7 - i) * a0 + i * a1 + 3) / 7);
        }
        else
        {
            for (var i = 1; i < 5; i++)
                ramp[i + 1] = (byte)(((5 - i) * a0 + i * a1 + 2) / 5);
            ramp[6] = 0;
            ramp[7] = 255;
        }

        ulong indices = 0;
        for (var i = 0; i < 6; i++)
            indices |= (ulong)data[2 + i] << (8 * i);
        for (var i = 0; i < 16; i++)
            alpha[i] = ramp[(int)((indices >> (3 * i)) & 7)];
    }

    private static void ApplyAlpha(Span<byte> block, ReadOnlySpan<byte> alpha)
    {
        for (var i = 0; i < 16; i++)
            block[i * 4 + 3] = alpha[i];
    }

    private static void Expand565(ushort color, Span<byte> bgra)
    {
        var r = (color >> 11) & 0x1F;
        var g = (color >> 5) & 0x3F;
        var b = color & 0x1F;
        bgra[0] = (byte)((b << 3) | (b >> 2));
        bgra[1] = (byte)((g << 2) | (g >> 4));
        bgra[2] = (byte)((r << 3) | (r >> 2));
        bgra[3] = 255;
    }

    /// <summary>Copies a 4×4 block, clipping it at the right and bottom edges.</summary>
    private static void CopyBlock(ReadOnlySpan<byte> block, byte[] output, int width, int height, int x0, int y0)
    {
        for (var y = 0; y < 4 && y0 + y < height; y++)
        {
            var count = Math.Min(4, width - x0);
            block.Slice(y * 16, count * 4).CopyTo(output.AsSpan(((y0 + y) * width + x0) * 4));
        }
    }

    /// <summary>RXGB stores red in the alpha channel (a DXT5 normal-map trick).</summary>
    private static DecodedImage SwizzleRxgb(DecodedImage image)
    {
        var p = image.Pixels;
        for (var i = 0; i < p.Length; i += 4)
        {
            p[i + 2] = p[i + 3];
            p[i + 3] = 255;
        }
        return image;
    }

    private static int ReadInt(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    /// <summary>Extracts one colour channel described by a bit mask and scales it to 8 bits.</summary>
    private readonly struct Channel(uint mask)
    {
        private readonly int _shift = mask == 0 ? 0 : BitOperations.TrailingZeroCount(mask);
        private readonly uint _max = mask == 0 ? 0 : mask >> BitOperations.TrailingZeroCount(mask);

        public byte Extract(uint value, byte fallback) =>
            _max == 0 ? fallback : (byte)((((value & mask) >> _shift) * 255 + _max / 2) / _max);
    }
}
