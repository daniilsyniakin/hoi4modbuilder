namespace HoI4ModBuilder.Game.Imaging;

/// <summary>Picks the right decoder for a texture file.</summary>
public static class TextureDecoder
{
    /// <summary>Formats the game uses that this library decodes itself.</summary>
    public static readonly IReadOnlySet<string> DecodedExtensions =
        new HashSet<string>([".dds", ".tga"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Formats any UI toolkit decodes natively.</summary>
    public static readonly IReadOnlySet<string> StandardExtensions =
        new HashSet<string>([".png", ".jpg", ".jpeg", ".bmp"], StringComparer.OrdinalIgnoreCase);

    public static bool IsImage(string path) =>
        DecodedExtensions.Contains(Path.GetExtension(path)) || StandardExtensions.Contains(Path.GetExtension(path));

    /// <summary>Decodes DDS or TGA. DDS is recognised by its signature whatever the extension says.</summary>
    /// <exception cref="InvalidDataException">Unsupported or corrupt data.</exception>
    public static DecodedImage Decode(ReadOnlySpan<byte> data, string path)
    {
        if (DdsDecoder.IsDds(data))
            return DdsDecoder.Decode(data);
        if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            return TgaDecoder.Decode(data);
        throw new InvalidDataException($"Формат файлу «{Path.GetFileName(path)}» не підтримується.");
    }
}
