using HoI4ModBuilder.Game.Imaging;

namespace HoI4ModBuilder.Game.Tests;

public sealed class DdsEncoderTests
{
    [Fact]
    public void RoundTripsThroughTheDecoder()
    {
        const int width = 3, height = 2;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i * 37 % 256);

        var file = DdsEncoder.Encode(width, height, pixels);
        var decoded = DdsDecoder.Decode(file);

        Assert.True(DdsDecoder.IsDds(file));
        Assert.Equal((width, height), (decoded.Width, decoded.Height));
        Assert.Equal(pixels, decoded.Pixels);
    }

    [Fact]
    public void WritesTheSameHeaderAsTheGamesOwnPictures()
    {
        var file = DdsEncoder.Encode(100, 88, new byte[100 * 88 * 4]);

        // Pixel format as in gfx/interface/goals/*.dds: 32 bytes, RGB | alpha, 32 bits, A8R8G8B8 masks.
        Assert.Equal(new uint[] { 32, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000 },
            Enumerable.Range(0, 8).Select(i => BitConverter.ToUInt32(file, 76 + i * 4)));
        Assert.Equal(128 + 100 * 88 * 4, file.Length);
    }

    [Fact]
    public void RejectsAWrongPixelCount() =>
        Assert.Throws<ArgumentException>(() => DdsEncoder.Encode(2, 2, new byte[3]));
}
