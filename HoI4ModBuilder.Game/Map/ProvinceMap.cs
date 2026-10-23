using System.Buffers.Binary;
using System.Globalization;

namespace HoI4ModBuilder.Game.Map;

public enum ProvinceType
{
    Land,
    Sea,
    Lake,
}

/// <summary>A line of <c>map/definition.csv</c>: the province's id, its colour in provinces.bmp and what it is.</summary>
public sealed record ProvinceInfo(int Id, int Rgb, ProvinceType Type, bool IsCoastal, string Terrain, int Continent);

/// <summary>
/// The map's provinces: <c>provinces.bmp</c> read into one province index per pixel, with the
/// provinces of <c>definition.csv</c>. Pixel rows go top to bottom.
/// </summary>
public sealed class ProvinceMap
{
    private readonly Dictionary<int, int> _indexById;

    private ProvinceMap(int width, int height, ushort[] pixels, IReadOnlyList<ProvinceInfo> provinces)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Provinces = provinces;
        _indexById = provinces.Select((p, i) => (p.Id, i)).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().i);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>For every pixel, the index of its province in <see cref="Provinces"/>.</summary>
    public ushort[] Pixels { get; }

    /// <summary>Index 0 is "no province" (colours not in definition.csv).</summary>
    public IReadOnlyList<ProvinceInfo> Provinces { get; }

    public int? IndexOf(int provinceId) => _indexById.TryGetValue(provinceId, out var index) ? index : null;

    /// <summary>The province at a pixel, or null outside the map.</summary>
    public ProvinceInfo? At(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? null : Pixels[y * Width + x] is var index and > 0 ? Provinces[index] : null;

    public static ProvinceMap Load(string bitmapPath, string definitionPath)
    {
        var provinces = new List<ProvinceInfo> { new(0, -1, ProvinceType.Land, false, "", 0) };
        var byColour = new Dictionary<int, ushort>();
        foreach (var line in File.ReadLines(definitionPath))
        {
            var parts = line.Split(';');
            if (parts.Length < 5 || !int.TryParse(parts[0], out var id) || id == 0
                || !byte.TryParse(parts[1], out var r) || !byte.TryParse(parts[2], out var g) || !byte.TryParse(parts[3], out var b))
                continue;
            var rgb = (r << 16) | (g << 8) | b;
            if (byColour.ContainsKey(rgb) || provinces.Count >= ushort.MaxValue)
                continue;
            var type = parts[4].Trim().ToLowerInvariant() switch
            {
                "sea" => ProvinceType.Sea,
                "lake" => ProvinceType.Lake,
                _ => ProvinceType.Land,
            };
            byColour[rgb] = (ushort)provinces.Count;
            provinces.Add(new ProvinceInfo(id, rgb, type,
                parts.Length > 5 && parts[5].Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
                parts.Length > 6 ? parts[6].Trim() : "",
                parts.Length > 7 && int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var continent) ? continent : 0));
        }

        var (width, height, pixels) = ReadBitmap(File.ReadAllBytes(bitmapPath), byColour);
        return new ProvinceMap(width, height, pixels, provinces);
    }

    /// <summary>An uncompressed 24- or 32-bit BMP, each colour looked up as a province.</summary>
    private static (int Width, int Height, ushort[] Pixels) ReadBitmap(byte[] data, Dictionary<int, ushort> byColour)
    {
        if (data.Length < 54 || data[0] != 'B' || data[1] != 'M')
            throw new InvalidDataException("provinces.bmp is not a BMP file.");
        var offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10));
        var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22));
        var bits = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(28));
        var compression = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(30));
        if (bits is not (24 or 32) || compression is not (0 or 3))
            throw new InvalidDataException($"provinces.bmp: {bits}-bit images with compression {compression} are not supported.");

        var height = Math.Abs(rawHeight);
        var bytesPerPixel = bits / 8;
        var stride = (width * bytesPerPixel + 3) & ~3;
        var pixels = new ushort[width * height];
        var lastColour = -1;
        ushort lastIndex = 0;
        for (var row = 0; row < height; row++)
        {
            // Rows are stored bottom-up unless the height is negative.
            var y = rawHeight > 0 ? height - 1 - row : row;
            var source = offset + row * stride;
            var target = y * width;
            for (var x = 0; x < width; x++, source += bytesPerPixel)
            {
                var colour = (data[source + 2] << 16) | (data[source + 1] << 8) | data[source];
                if (colour != lastColour)
                {
                    lastColour = colour;
                    lastIndex = byColour.GetValueOrDefault(colour);
                }
                pixels[target + x] = lastIndex;
            }
        }
        return (width, height, pixels);
    }
}
