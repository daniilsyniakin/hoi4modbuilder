using System.Buffers.Binary;

namespace HoI4ModBuilder.Game.Imaging;

/// <summary>
/// Writes uncompressed 32-bit DDS files — the format nearly all of the game's interface pictures
/// use (A8R8G8B8, no mipmaps), so no quality is lost and the game reads them as it reads its own.
/// </summary>
public static class DdsEncoder
{
    private const int HeaderSize = 4 + 124;

    // DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PITCH | DDSD_PIXELFORMAT
    private const uint HeaderFlags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000;
    // DDPF_RGB | DDPF_ALPHAPIXELS
    private const uint PixelFormatFlags = 0x40 | 0x1;
    private const uint CapsTexture = 0x1000;

    /// <param name="bgra">Straight (not premultiplied) alpha, 4 bytes per pixel in B, G, R, A order, rows top to bottom.</param>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> bgra)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (bgra.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes of pixels, got {bgra.Length}.", nameof(bgra));

        var file = new byte[HeaderSize + bgra.Length];
        "DDS "u8.CopyTo(file);
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), value);

        Write(4, 124);                  // dwSize
        Write(8, HeaderFlags);
        Write(12, (uint)height);
        Write(16, (uint)width);
        Write(20, (uint)(width * 4));   // dwPitchOrLinearSize
        // depth, mipmap count and dwReserved1[11] stay zero
        Write(76, 32);                  // ddspf.dwSize
        Write(80, PixelFormatFlags);
        Write(88, 32);                  // dwRGBBitCount
        Write(92, 0x00FF0000);          // R
        Write(96, 0x0000FF00);          // G
        Write(100, 0x000000FF);         // B
        Write(104, 0xFF000000);         // A
        Write(108, CapsTexture);

        // A8R8G8B8 in little-endian memory is exactly B, G, R, A.
        bgra.CopyTo(file.AsSpan(HeaderSize));
        return file;
    }
}
