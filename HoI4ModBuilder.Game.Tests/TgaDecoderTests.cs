using HoI4ModBuilder.Game.Imaging;

namespace HoI4ModBuilder.Game.Tests;

public class TgaDecoderTests
{
    private static byte[] Tga(int type, int width, int height, int depth, int descriptor, byte[] data, byte[]? palette = null, int paletteDepth = 24)
    {
        var header = new byte[18];
        header[2] = (byte)type;
        if (palette is not null)
        {
            header[1] = 1;
            var entries = palette.Length / (paletteDepth / 8);
            header[5] = (byte)entries;
            header[7] = (byte)paletteDepth;
        }
        header[12] = (byte)width;
        header[14] = (byte)height;
        header[16] = (byte)depth;
        header[17] = (byte)descriptor;
        return [.. header, .. palette ?? [], .. data];
    }

    private static (byte B, byte G, byte R, byte A) Pixel(DecodedImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return (image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }

    [Fact]
    public void BottomUpIsFlipped()
    {
        // 1×2, stored bottom row first.
        var image = TgaDecoder.Decode(Tga(2, 1, 2, 32, 0x08, [1, 1, 1, 255, 2, 2, 2, 255]));
        Assert.Equal((2, 2, 2, 255), Pixel(image, 0, 0));
        Assert.Equal((1, 1, 1, 255), Pixel(image, 0, 1));
    }

    [Fact]
    public void TopDownIsKept()
    {
        var image = TgaDecoder.Decode(Tga(2, 1, 2, 32, 0x28, [1, 1, 1, 255, 2, 2, 2, 255]));
        Assert.Equal((1, 1, 1, 255), Pixel(image, 0, 0));
    }

    [Fact]
    public void ZeroAlphaWithoutAttributeBitsMeansOpaque()
    {
        var image = TgaDecoder.Decode(Tga(2, 2, 1, 32, 0x20, [1, 2, 3, 0, 4, 5, 6, 0]));
        Assert.Equal((1, 2, 3, 255), Pixel(image, 0, 0));
    }

    [Fact]
    public void RealAlphaIsKept()
    {
        var image = TgaDecoder.Decode(Tga(2, 2, 1, 32, 0x28, [1, 2, 3, 0, 4, 5, 6, 128]));
        Assert.Equal(0, Pixel(image, 0, 0).A);
        Assert.Equal(128, Pixel(image, 1, 0).A);
    }

    [Fact]
    public void Rle24Bit()
    {
        // Run of 3 red pixels, then a raw packet with 1 blue pixel.
        var image = TgaDecoder.Decode(Tga(10, 4, 1, 24, 0x20, [0x82, 0, 0, 255, 0x00, 255, 0, 0]));
        Assert.Equal((0, 0, 255, 255), Pixel(image, 2, 0));
        Assert.Equal((255, 0, 0, 255), Pixel(image, 3, 0));
    }

    [Fact]
    public void Greyscale()
    {
        var image = TgaDecoder.Decode(Tga(3, 1, 1, 8, 0x20, [77]));
        Assert.Equal((77, 77, 77, 255), Pixel(image, 0, 0));
    }

    [Fact]
    public void ColourMapped()
    {
        var palette = new byte[] { 0, 0, 255, 0, 255, 0 }; // red, green (BGR)
        var image = TgaDecoder.Decode(Tga(1, 2, 1, 8, 0x20, [1, 0], palette));
        Assert.Equal((0, 255, 0, 255), Pixel(image, 0, 0));
        Assert.Equal((0, 0, 255, 255), Pixel(image, 1, 0));
    }

    [Fact]
    public void SixteenBit()
    {
        // 0x7C00 = red, no alpha attribute bits → opaque.
        var image = TgaDecoder.Decode(Tga(2, 1, 1, 16, 0x20, [0x00, 0x7C]));
        Assert.Equal((0, 0, 255, 255), Pixel(image, 0, 0));
    }

    [Fact]
    public void TruncatedDataIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => TgaDecoder.Decode(Tga(2, 10, 10, 32, 0x20, [1, 2, 3])));
    }
}
