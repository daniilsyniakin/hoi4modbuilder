using System.Text;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Sprites;

/// <summary>What a picture is for, which decides its size, folder and sprite name.</summary>
public enum SpriteImportKind
{
    Focus,
    Idea,
    Decision,
    EventPicture,
    NewsPicture,
}

/// <param name="Prefix">Sprite names start with it: <c>GFX_focus_</c>, <c>GFX_idea_</c>…</param>
/// <param name="Width">The size the game's own pictures of this kind have (measured over vanilla).</param>
/// <param name="TextureFolder">Where the game keeps such textures.</param>
/// <param name="GfxName">The mod's .gfx file (in <c>interface/</c>) new sprites of this kind are added to, after the mod's name.</param>
public sealed record SpriteImportSpec(string Prefix, int Width, int Height, string TextureFolder, string GfxName);

/// <summary>Turning a picture file into a sprite the game knows.</summary>
public static class SpriteImport
{
    public static SpriteImportSpec Spec(SpriteImportKind kind) => kind switch
    {
        SpriteImportKind.Focus => new("GFX_focus_", 100, 88, "gfx/interface/goals", "goals"),
        SpriteImportKind.Idea => new("GFX_idea_", 60, 68, "gfx/interface/ideas", "ideas"),
        SpriteImportKind.Decision => new("GFX_decision_", 33, 32, "gfx/interface/decisions", "decisions"),
        SpriteImportKind.EventPicture => new("GFX_report_event_", 210, 176, "gfx/event_pictures", "eventpictures"),
        SpriteImportKind.NewsPicture => new("GFX_news_event_", 397, 153, "gfx/event_pictures", "eventpictures"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The kind a sprite picker offers, from the prefixes it lists.</summary>
    public static SpriteImportKind? KindFor(IEnumerable<string> prefixes) => prefixes.FirstOrDefault() switch
    {
        "GFX_goal_" or "GFX_focus_" => SpriteImportKind.Focus,
        "GFX_idea_" => SpriteImportKind.Idea,
        "GFX_decision_" => SpriteImportKind.Decision,
        "GFX_report_event_" => SpriteImportKind.EventPicture,
        "GFX_news_event_" => SpriteImportKind.NewsPicture,
        _ => null,
    };

    /// <summary>A sprite/file name from a picture's file name: <c>My Army (2).png</c> → <c>my_army_2</c>.</summary>
    public static string NameFrom(string fileName)
    {
        var builder = new StringBuilder();
        foreach (var c in Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant())
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        var name = string.Join('_', builder.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? "picture" : name;
    }

    /// <summary>
    /// The part of a <paramref name="sourceWidth"/>×<paramref name="sourceHeight"/> picture to scale
    /// into the target so it fills it without stretching: the centre, cut to the target's proportions.
    /// </summary>
    public static (int X, int Y, int Width, int Height) CoverCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var scale = Math.Max((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
        var width = Math.Min(sourceWidth, (int)Math.Round(targetWidth / scale));
        var height = Math.Min(sourceHeight, (int)Math.Round(targetHeight / scale));
        return ((sourceWidth - width) / 2, (sourceHeight - height) / 2, width, height);
    }

    /// <summary>
    /// Adds <c>spriteType = { name texturefile }</c> to a .gfx file, inside its <c>spriteTypes</c>
    /// block (created when the file has none). Applied to an empty text it makes a whole file.
    /// </summary>
    public static IReadOnlyList<TextEdit> AddSpriteType(string text, string name, string textureFile)
    {
        var tree = SyntaxTree.Parse(text);
        var sprite = $"spriteType = {{\n\tname = \"{name}\"\n\ttexturefile = \"{textureFile}\"\n}}";
        var block = tree.Root.Properties()
            .FirstOrDefault(p => p.Key.ValueText.Equals("spriteTypes", StringComparison.OrdinalIgnoreCase))?.Value as BlockSyntax;
        return block is not null
            ? [ScriptEdits.InsertMultiLine(text, block, sprite)]
            : [ScriptEdits.InsertMultiLine(text, tree.Root, "spriteTypes = {\n\t" + sprite.Replace("\n", "\n\t") + "\n}")];
    }
}
