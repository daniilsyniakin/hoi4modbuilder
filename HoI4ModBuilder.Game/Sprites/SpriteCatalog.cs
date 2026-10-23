using System.Text;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.FileSystem;

namespace HoI4ModBuilder.Game.Sprites;

public enum SpriteKind
{
    /// <summary><c>spriteType</c>: a plain image, optionally split into frames (e.g. button states).</summary>
    Sprite,
    /// <summary><c>frameAnimatedSpriteType</c>: a strip of animation frames.</summary>
    FrameAnimated,
    /// <summary><c>corneredTileSpriteType</c>: a 9-slice background.</summary>
    CorneredTile,
    /// <summary><c>textSpriteType</c>: a button background with frames.</summary>
    Text,
    /// <summary><c>maskedShieldType</c>: an image shown through a mask (unit shields).</summary>
    MaskedShield,
    /// <summary>Progress bars, charts and other special types that have a name and a texture.</summary>
    Other,
}

/// <param name="Name">The <c>GFX_…</c> name scripts refer to.</param>
/// <param name="TextureFile">Path relative to the game root, as written in the .gfx file.</param>
/// <param name="Frames">Horizontal frames in the texture; the first one is the "normal" look.</param>
/// <param name="SourceFile">The .gfx file that defines it, relative to the game root.</param>
/// <param name="Position">Offset of the definition in <paramref name="SourceFile"/>, for go-to-definition.</param>
public sealed record SpriteDefinition(
    string Name,
    SpriteKind Kind,
    string? TextureFile,
    int Frames,
    string SourceFile,
    FileLayer Layer,
    int Position);

/// <summary>
/// All sprites the game would know with the mod loaded: every <c>interface/**/*.gfx</c> of the
/// merged file system. When a name is defined twice, the later file (by path, mod after vanilla)
/// wins.
/// </summary>
public sealed class SpriteCatalog
{
    // Per file, in priority order (lowest first), so one changed file can be re-read alone.
    private readonly IReadOnlyList<(VirtualFile File, IReadOnlyList<SpriteDefinition> Sprites)> _files;
    private readonly Dictionary<string, SpriteDefinition> _sprites = new(StringComparer.OrdinalIgnoreCase);

    private SpriteCatalog(IReadOnlyList<(VirtualFile File, IReadOnlyList<SpriteDefinition> Sprites)> files)
    {
        _files = files;
        foreach (var definition in files.SelectMany(f => f.Sprites))
            _sprites[definition.Name] = definition;
    }

    public static SpriteCatalog Empty { get; } = new([]);

    public int Count => _sprites.Count;

    public IEnumerable<SpriteDefinition> All => _sprites.Values;

    public SpriteDefinition? Find(string name) => _sprites.GetValueOrDefault(name);

    public static SpriteCatalog Load(GameFileSystem fileSystem)
    {
        var files = fileSystem.EnumerateFiles("interface", "*.gfx")
            .OrderBy(f => f.Layer)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Parsing is the expensive part and files are independent.
        var parsed = new IReadOnlyList<SpriteDefinition>[files.Count];
        Parallel.For(0, files.Count, i => parsed[i] = ParseFile(files[i]));
        return new SpriteCatalog(files.Select((f, i) => (f, parsed[i])).ToList());
    }

    /// <summary>
    /// The catalog with one .gfx file re-read (or dropped, if it is gone) — a few milliseconds
    /// instead of re-reading the whole game.
    /// </summary>
    public SpriteCatalog WithFile(GameFileSystem fileSystem, string relativePath)
    {
        var relative = GameFileSystem.NormalizeRelative(relativePath);
        var files = _files.Where(f => !f.File.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)).ToList();
        if (fileSystem.Resolve(relative) is { } file)
            files.Add((file, ParseFile(file)));
        return new SpriteCatalog(files
            .OrderBy(f => f.File.Layer)
            .ThenBy(f => f.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static IReadOnlyList<SpriteDefinition> ParseFile(VirtualFile file)
    {
        try
        {
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(file.FullPath));
            return Parse(SyntaxTree.Parse(text), file.RelativePath, file.Layer).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return []; // A file that can't be read contributes nothing.
        }
    }

    /// <summary>Sprite definitions in one parsed .gfx file.</summary>
    public static IEnumerable<SpriteDefinition> Parse(SyntaxTree tree, string sourceFile, FileLayer layer)
    {
        foreach (var group in Blocks(tree.Root, "spriteTypes"))
        {
            foreach (var property in group.Properties())
            {
                if (property.Value is not BlockSyntax block)
                    continue;

                var kind = KindOf(property.Key.ValueText);
                var name = Scalar(block, "name");
                if (kind is null || string.IsNullOrEmpty(name))
                    continue;

                var texture = Scalar(block, "texturefile") ?? Scalar(block, "textureFile1");
                var frames = int.TryParse(Scalar(block, "noOfFrames"), out var n) && n > 0 ? n : 1;
                yield return new SpriteDefinition(name, kind.Value, texture, frames, sourceFile, layer, property.Span.Start);
            }
        }
    }

    private static SpriteKind? KindOf(string key) => key.ToLowerInvariant() switch
    {
        "spritetype" => SpriteKind.Sprite,
        "frameanimatedspritetype" => SpriteKind.FrameAnimated,
        "corneredtilespritetype" => SpriteKind.CorneredTile,
        "textspritetype" => SpriteKind.Text,
        "maskedshieldtype" => SpriteKind.MaskedShield,
        "progressbartype" or "circularprogressbartype" or "piecharttype" or "linecharttype" or "arrowtype" => SpriteKind.Other,
        _ => null,
    };

    // Clausewitz keys are case-insensitive: the game ships both "spriteType" and "SpriteType".
    private static IEnumerable<BlockSyntax> Blocks(IStatementContainer container, string key) =>
        container.Properties()
            .Where(p => p.Key.ValueText.Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value)
            .OfType<BlockSyntax>();

    private static string? Scalar(IStatementContainer container, string key) =>
        container.Properties()
            .LastOrDefault(p => p.Key.ValueText.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value is ScalarSyntax { Token.IsMissing: false } scalar
            ? scalar.ValueText
            : null;
}
