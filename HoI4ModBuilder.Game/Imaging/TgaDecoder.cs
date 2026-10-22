using System.Buffers.Binary;

namespace HoI4ModBuilder.Game.Imaging;

/// <summary>
/// Decodes Truevision TGA: true-colour, grey-scale and colour-mapped images, raw or RLE,
/// 8/15/16/24/32 bits, either origin. HoI4 uses it mostly for country flags.
/// </summary>
public static class TgaDecoder
{
    private const int HeaderSize = 18;

    /// <exception cref="InvalidDataException">The data is not a supported TGA image.</exception>
    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Не TGA-файл.");

        int idLength = data[0];
        int colorMapType = data[1];
        int imageType = data[2];
        int colorMapStart = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]);
        int colorMapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
        int colorMapDepth = data[7];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        int depth = data[16];
        int descriptor = data[17];

        var isRle = imageType is 9 or 10 or 11;
        var kind = imageType & 7; // 1 colour-mapped, 2 true-colour, 3 grey
        if (kind is not (1 or 2 or 3) || width == 0 || height == 0)
            throw new InvalidDataException($"Тип TGA {imageType} не підтримується.");
        if (depth is not (8 or 15 or 16 or 24 or 32))
            throw new InvalidDataException($"Глибина кольору TGA {depth} біт не підтримується.");

        var position = HeaderSize + idLength;
        byte[]? palette = null;
        if (colorMapType == 1)
        {
            var entrySize = (colorMapDepth + 7) / 8;
            var mapBytes = colorMapLength * entrySize;
            if (data.Length < position + mapBytes)
                throw new InvalidDataException("TGA-файл обрізаний.");
            palette = new byte[(colorMapStart + colorMapLength) * 4];
            for (var i = 0; i < colorMapLength; i++)
                ReadPixel(data.Slice(position + i * entrySize, entrySize), colorMapDepth, 2, palette.AsSpan((colorMapStart + i) * 4, 4));
            position += mapBytes;
        }
        if (kind == 1 && palette is null)
            throw new InvalidDataException("TGA з палітрою не містить палітри.");

        var bytesPerPixel = (depth + 7) / 8;
        var pixelCount = width * height;
        // Decode in file order first; orientation is fixed afterwards.
        var raw = new byte[pixelCount * 4];
        var source = data[position..];
        var read = 0;
        var written = 0;
        Span<byte> pixel = stackalloc byte[4];

        while (written < pixelCount)
        {
            if (!isRle)
            {
                Take(source, ref read, bytesPerPixel, kind, depth, palette, raw.AsSpan(written * 4, 4));
                written++;
                continue;
            }

            if (read >= source.Length)
                throw new InvalidDataException("TGA-файл обрізаний.");
            var header = source[read++];
            var count = (header & 0x7F) + 1;
            if (written + count > pixelCount)
                count = pixelCount - written; // tolerate packets that overrun the image
            if ((header & 0x80) != 0)
            {
                Take(source, ref read, bytesPerPixel, kind, depth, palette, pixel);
                for (var i = 0; i < count; i++)
                    pixel.CopyTo(raw.AsSpan((written + i) * 4, 4));
            }
            else
            {
                for (var i = 0; i < count; i++)
                    Take(source, ref read, bytesPerPixel, kind, depth, palette, raw.AsSpan((written + i) * 4, 4));
            }
            written += count;
        }

        var attributeBits = descriptor & 0x0F;
        if (depth == 32 && attributeBits == 0 && AlphaIsAllZero(raw))
            MakeOpaque(raw); // many exporters write 32-bit TGAs with an unused, zeroed alpha channel
        if (depth is 15 or 16 && attributeBits == 0)
            MakeOpaque(raw);

        var pixels = Orient(raw, width, height, rightToLeft: (descriptor & 0x10) != 0, topToBottom: (descriptor & 0x20) != 0);
        var format = $"{(kind == 1 ? "Indexed" : kind == 3 ? "Grey" : "RGB")} {depth}-bit{(isRle ? " RLE" : "")}";
        return new DecodedImage(width, height, pixels, format);
    }

    private static void Take(
        ReadOnlySpan<byte> source, ref int read, int bytesPerPixel, int kind, int depth, byte[]? palette, Span<byte> output)
    {
        if (read + bytesPerPixel > source.Length)
            throw new InvalidDataException("TGA-файл обрізаний.");
        var bytes = source.Slice(read, bytesPerPixel);
        read += bytesPerPixel;

        if (kind == 1)
        {
            var index = bytesPerPixel == 1 ? bytes[0] : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            if ((index + 1) * 4 > palette!.Length)
                throw new InvalidDataException("Індекс поза палітрою TGA.");
            palette.AsSpan(index * 4, 4).CopyTo(output);
            return;
        }
        ReadPixel(bytes, depth, kind, output);
    }

    /// <summary>Converts one stored pixel (BGR order in the file) to BGRA.</summary>
    private static void ReadPixel(ReadOnlySpan<byte> bytes, int depth, int kind, Span<byte> bgra)
    {
        switch (depth)
        {
            case 8:
                bgra[0] = bgra[1] = bgra[2] = bytes[0];
                bgra[3] = 255;
                break;
            case 15 or 16:
                var v = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
                bgra[0] = Scale5(v & 0x1F);
                bgra[1] = Scale5((v >> 5) & 0x1F);
                bgra[2] = Scale5((v >> 10) & 0x1F);
                bgra[3] = (v & 0x8000) != 0 ? (byte)255 : (byte)0;
                break;
            case 24:
                bgra[0] = bytes[0];
                bgra[1] = bytes[1];
                bgra[2] = bytes[2];
                bgra[3] = 255;
                break;
            default:
                bytes[..4].CopyTo(bgra);
                break;
        }
    }

    private static byte Scale5(int v) => (byte)((v << 3) | (v >> 2));

    private static bool AlphaIsAllZero(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
                return false;
        }
        return true;
    }

    private static void MakeOpaque(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
    }

    private static byte[] Orient(byte[] raw, int width, int height, bool rightToLeft, bool topToBottom)
    {
        if (topToBottom && !rightToLeft)
            return raw;

        var output = new byte[raw.Length];
        var stride = width * 4;
        for (var y = 0; y < height; y++)
        {
            var sourceRow = raw.AsSpan(y * stride, stride);
            var targetRow = output.AsSpan((topToBottom ? y : height - 1 - y) * stride, stride);
            if (!rightToLeft)
            {
                sourceRow.CopyTo(targetRow);
                continue;
            }
            for (var x = 0; x < width; x++)
                sourceRow.Slice(x * 4, 4).CopyTo(targetRow.Slice((width - 1 - x) * 4, 4));
        }
        return output;
    }
}
