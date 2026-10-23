using HoI4ModBuilder.Game.Map;

namespace HoI4ModBuilder.ViewModels;

/// <summary>Paints the province map: a colour per province, darker lines where states and countries meet.</summary>
public static class MapPainter
{
    public const int Sea = 0x1C2531;
    public const int Lake = 0x223042;
    public const int NoState = 0x3A3C40;

    /// <param name="colour">Colour of each province (by index in <see cref="ProvinceMap.Provinces"/>), 0xRRGGBB.</param>
    /// <param name="area">What a province belongs to (a state, a region); a line is drawn where it changes. 0 = none.</param>
    /// <param name="country">Its country; a stronger line where it changes.</param>
    /// <param name="selected">The area to highlight, or 0.</param>
    /// <returns>BGRA pixels, rows top to bottom.</returns>
    public static int[] Paint(ProvinceMap map, int[] colour, int[] area, int[] country, int selected)
    {
        var width = map.Width;
        var height = map.Height;
        var pixels = map.Pixels;
        var result = new int[width * height];
        Parallel.For(0, height, y =>
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var i = row + x;
                var p = pixels[i];
                var c = colour[p];
                var a = area[p];
                if (a != 0)
                {
                    // Compare with the right and lower neighbours: one line per border.
                    var right = x + 1 < width ? pixels[i + 1] : p;
                    var below = y + 1 < height ? pixels[i + width] : p;
                    var countryBorder = country[right] != country[p] || country[below] != country[p];
                    var areaBorder = area[right] != a || area[below] != a;
                    var leftSelected = selected != 0 && (area[right] == selected || area[below] == selected) && a != selected;
                    if (selected != 0 && (a == selected && areaBorder || leftSelected))
                        c = 0xFFFFFF;
                    else if (countryBorder)
                        c = Darken(c, 0.35);
                    else if (areaBorder)
                        c = Darken(c, 0.68);
                    else if (a == selected && selected != 0)
                        c = Mix(c, 0xFFFFFF, 0.25);
                }
                result[i] = unchecked((int)0xFF000000) | c;
            }
        });
        return result;
    }

    public static int Darken(int rgb, double factor) =>
        ((int)(((rgb >> 16) & 0xFF) * factor) << 16) | ((int)(((rgb >> 8) & 0xFF) * factor) << 8) | (int)((rgb & 0xFF) * factor);

    /// <summary><paramref name="a"/> moved towards <paramref name="b"/> by <paramref name="amount"/> (0–1).</summary>
    public static int Mix(int a, int b, double amount)
    {
        int Channel(int shift) => (int)Math.Round(((a >> shift) & 0xFF) * (1 - amount) + ((b >> shift) & 0xFF) * amount);
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    /// <summary>A stable, pleasant colour for a number (strategic regions).</summary>
    public static int Hashed(int seed)
    {
        var hue = (seed * 0.61803398875) % 1;
        var (r, g, b) = Hsv(hue, 0.45, 0.75);
        return ((int)(r * 255) << 16) | ((int)(g * 255) << 8) | (int)(b * 255);
    }

    private static (double, double, double) Hsv(double h, double s, double v)
    {
        h *= 6;
        var i = (int)Math.Floor(h) % 6;
        var f = h - Math.Floor(h);
        var (p, q, t) = (v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f)));
        return i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }
}
