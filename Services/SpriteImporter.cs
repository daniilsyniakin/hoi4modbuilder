using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Game.Imaging;
using HoI4ModBuilder.Game.Sprites;
using SkiaSharp;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Turns a picture (PNG, JPG…) into a sprite of the mod: cut and scaled to the size the game uses
/// for that kind, saved as DDS where the game keeps such pictures, and registered in the mod's
/// .gfx — all as one undoable action.
/// </summary>
public sealed class SpriteImporter(ScriptWorkspace workspace, GameAssets assets, string modName)
{
    /// <summary>Picture files that can be imported.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif"];

    public static bool CanImport(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <returns>The new sprite's name, or an error message.</returns>
    public (string? Sprite, string? Error) Import(string imagePath, SpriteImportKind kind)
    {
        var spec = SpriteImport.Spec(kind);
        byte[]? pixels;
        try
        {
            pixels = Render(imagePath, spec.Width, spec.Height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            pixels = null;
        }
        if (pixels is null)
            return (null, $"Не вдалося прочитати картинку «{Path.GetFileName(imagePath)}».");

        // A name not taken by the game's sprites, the mod's sprites or an existing texture.
        var name = SpriteImport.NameFrom(imagePath);
        var baseName = name;
        for (var n = 2; assets.Sprites.Find(spec.Prefix + baseName) is not null
                        || File.Exists(Path.Combine(workspace.RootPath, spec.TextureFolder, baseName + ".dds")); n++)
            baseName = $"{name}_{n}";

        var texture = $"{spec.TextureFolder}/{baseName}.dds";
        var sprite = spec.Prefix + baseName;
        var gfx = Path.Combine(workspace.RootPath, "interface", $"{ModNames.Slug(modName, workspace.RootPath)}_{spec.GfxName}.gfx");

        using var action = workspace.Action($"Картинка «{baseName}»");
        var error = workspace.CreateBinaryFile(Path.Combine(workspace.RootPath, texture), DdsEncoder.Encode(spec.Width, spec.Height, pixels))
                    ?? (File.Exists(gfx)
                        ? workspace.Edit(gfx, (text, _) => SpriteImport.AddSpriteType(text, sprite, texture))
                        : workspace.CreateFile(gfx, ScriptEdits.Apply("", SpriteImport.AddSpriteType("", sprite, texture)), withByteOrderMark: false));
        return error is null ? (sprite, null) : (null, error);
    }

    /// <summary>
    /// The picture's centre, cut to the target's proportions and scaled to its size, as straight-alpha
    /// BGRA; null when the file isn't a picture Skia can read.
    /// </summary>
    private static byte[]? Render(string path, int width, int height)
    {
        using var source = SKBitmap.Decode(path);
        if (source is null)
            return null;

        var (x, y, w, h) = SpriteImport.CoverCrop(source.Width, source.Height, width, height);
        using var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.Transparent);
            // Mipmapped sampling keeps big photos from shimmering when shrunk to icon size.
            canvas.DrawImage(image, new SKRect(x, y, x + w, y + h), new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        // The canvas works premultiplied; DDS wants straight alpha.
        var pixels = target.GetPixelSpan().ToArray();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha is 0 or 255)
                continue;
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Min(255, pixels[i + c] * 255 / alpha);
        }
        return pixels;
    }
}
