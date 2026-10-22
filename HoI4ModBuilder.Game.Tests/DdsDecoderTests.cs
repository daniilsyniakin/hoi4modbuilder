using System.Buffers.Binary;
using HoI4ModBuilder.Game.Imaging;

namespace HoI4ModBuilder.Game.Tests;

public class DdsDecoderTests
{
    /// <summary>Builds a DDS file in memory.</summary>
    private static byte[] Dds(
        int width, int height, byte[] data,
        string? fourCc = null, uint flags = 0, int bits = 0,
        uint r = 0, uint g = 0, uint b = 0, uint a = 0, uint? dxgi = null)
    {
        var headerSize = 128 + (dxgi is null ? 0 : 20);
        var file = new byte[headerSize + data.Length];
        "DDS "u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 124);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(12), height);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(76), 32);
        if (fourCc is not null)
        {
            flags |= 0x4;
            System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(file, 84);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(80), flags);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(88), bits);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(92), r);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(96), g);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(100), b);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(104), a);
        if (dxgi is not null)
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(128), dxgi.Value);
        data.CopyTo(file, headerSize);
        return file;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(DecodedImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return (image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }

    [Fact]
    public void Uncompressed32BitBgra()
    {
        // Little-endian ARGB words: stored as B, G, R, A.
        var data = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        var image = DdsDecoder.Decode(Dds(2, 1, data, flags: 0x41, bits: 32, r: 0xFF0000, g: 0xFF00, b: 0xFF, a: 0xFF000000));

        Assert.Equal((10, 20, 30, 40), Pixel(image, 0, 0));
        Assert.Equal((50, 60, 70, 80), Pixel(image, 1, 0));
    }

    [Fact]
    public void Uncompressed24BitIsOpaque()
    {
        var image = DdsDecoder.Decode(Dds(1, 1, [1, 2, 3], flags: 0x40, bits: 24, r: 0xFF0000, g: 0xFF00, b: 0xFF));
        Assert.Equal((1, 2, 3, 255), Pixel(image, 0, 0));
    }

    [Fact]
    public void X1R5G5B5IgnoresTheSpareBit()
    {
        // Pure red with the spare bit clear: 0b0_11111_00000_00000.
        var image = DdsDecoder.Decode(Dds(1, 1, [0x00, 0x7C], flags: 0x40, bits: 16, r: 0x7C00, g: 0x3E0, b: 0x1F));
        Assert.Equal((0, 0, 255, 255), Pixel(image, 0, 0));
    }

    [Fact]
    public void Dx10Rgba8()
    {
        var image = DdsDecoder.Decode(Dds(1, 1, [1, 2, 3, 4], fourCc: "DX10", dxgi: 29));
        Assert.Equal((3, 2, 1, 4), Pixel(image, 0, 0));
    }

    [Fact]
    public void Dxt1FourColorBlock()
    {
        // c0 = white, c1 = black; indices: pixel 0 → c0, 1 → c1, 2 → 2/3 c0, 3 → 1/3 c0.
        var block = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0b11_10_01_00, 0, 0, 0 };
        var image = DdsDecoder.Decode(Dds(4, 4, block, fourCc: "DXT1"));

        Assert.Equal((255, 255, 255, 255), Pixel(image, 0, 0));
        Assert.Equal((0, 0, 0, 255), Pixel(image, 1, 0));
        Assert.Equal((170, 170, 170, 255), Pixel(image, 2, 0));
        Assert.Equal((85, 85, 85, 255), Pixel(image, 3, 0));
        Assert.Equal("DXT1", image.Format);
    }

    [Fact]
    public void Dxt1ThreeColorBlockHasTransparency()
    {
        // c0 <= c1 selects 3-colour mode: index 3 is transparent black.
        var block = new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0b11_10_00_01, 0, 0, 0 };
        var image = DdsDecoder.Decode(Dds(4, 4, block, fourCc: "DXT1"));

        Assert.Equal((255, 255, 255, 255), Pixel(image, 0, 0));
        Assert.Equal((127, 127, 127, 255), Pixel(image, 2, 0));
        Assert.Equal((0, 0, 0, 0), Pixel(image, 3, 0));
    }

    [Fact]
    public void Dxt5InterpolatedAlpha()
    {
        // Alpha endpoints 255 and 0 (8-value ramp); pixel 0 → index 0, pixel 1 → index 1, pixel 2 → index 2.
        var alphaIndices = 0b010_001_000;
        var block = new byte[16];
        block[0] = 255;
        block[1] = 0;
        block[2] = (byte)alphaIndices;
        block[3] = (byte)(alphaIndices >> 8);
        block[8] = block[9] = 0xFF; // colour c0 = white, all indices 0
        var image = DdsDecoder.Decode(Dds(4, 4, block, fourCc: "DXT5"));

        Assert.Equal(255, Pixel(image, 0, 0).A);
        Assert.Equal(0, Pixel(image, 1, 0).A);
        Assert.Equal(219, Pixel(image, 2, 0).A); // (6*255 + 1*0 + 3) / 7
        Assert.Equal((255, 255, 255), (Pixel(image, 0, 0).B, Pixel(image, 0, 0).G, Pixel(image, 0, 0).R));
    }

    [Fact]
    public void Dxt3ExplicitAlpha()
    {
        var block = new byte[16];
        block[0] = 0xF0; // pixel 0 alpha 0, pixel 1 alpha 15
        block[8] = block[9] = 0xFF;
        var image = DdsDecoder.Decode(Dds(4, 4, block, fourCc: "DXT3"));

        Assert.Equal(0, Pixel(image, 0, 0).A);
        Assert.Equal(255, Pixel(image, 1, 0).A);
    }

    [Fact]
    public void BlockTexturesThatArentMultiplesOfFourAreClipped()
    {
        // 5×3 needs 2×1 blocks.
        var blocks = new byte[16];
        blocks[0] = blocks[1] = 0xFF; // first block white
        var image = DdsDecoder.Decode(Dds(5, 3, blocks, fourCc: "DXT1"));

        Assert.Equal(5, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal((255, 255, 255, 255), Pixel(image, 3, 2));
        Assert.Equal((0, 0, 0, 255), Pixel(image, 4, 2));
    }

    [Theory]
    [InlineData("DXT1")]
    [InlineData("DXT5")]
    public void TruncatedDataIsRejected(string fourCc)
    {
        Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(Dds(64, 64, new byte[10], fourCc: fourCc)));
    }

    [Fact]
    public void UnknownFormatIsRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(Dds(4, 4, new byte[64], fourCc: "ATI2")));
        Assert.Contains("ATI2", error.Message);
    }

    [Fact]
    public void NotADds()
    {
        Assert.False(DdsDecoder.IsDds("PNG!"u8));
        Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(new byte[200]));
    }
}
