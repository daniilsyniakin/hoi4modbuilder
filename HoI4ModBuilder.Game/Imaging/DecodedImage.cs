namespace HoI4ModBuilder.Game.Imaging;

/// <summary>
/// A decoded texture: 32-bit BGRA, straight (non-premultiplied) alpha, rows top to bottom,
/// stride = <see cref="Width"/> * 4. This is what UI frameworks can upload directly.
/// </summary>
public sealed class DecodedImage
{
    public DecodedImage(int width, int height, byte[] pixels, string format)
    {
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer size doesn't match the dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
        Format = format;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>Human-readable source format, e.g. <c>DXT5</c> or <c>BGRA8</c>.</summary>
    public string Format { get; }
}
