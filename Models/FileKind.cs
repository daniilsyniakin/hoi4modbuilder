namespace HoI4ModBuilder.Models;

/// <summary>Coarse file categories used for icons and, later, for picking an editor.</summary>
public enum FileKind
{
    Folder,
    Script,
    Localisation,
    Interface,
    Image,
    Descriptor,
    Data,
    Other,
}

public static class FileKinds
{
    /// <summary>Extensions that hold Clausewitz script (parsed, highlighted, completed).</summary>
    public static readonly IReadOnlySet<string> ScriptExtensions =
        new HashSet<string>([".txt", ".gui", ".gfx", ".asset", ".mod"], StringComparer.OrdinalIgnoreCase);

    public static bool IsScript(string path) => ScriptExtensions.Contains(Path.GetExtension(path));

    public static FileKind FromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".txt" => FileKind.Script,
        ".yml" => FileKind.Localisation,
        ".gui" or ".gfx" => FileKind.Interface,
        ".dds" or ".png" or ".tga" or ".bmp" or ".jpg" or ".jpeg" => FileKind.Image,
        ".mod" => FileKind.Descriptor,
        ".csv" or ".json" or ".lua" or ".asset" or ".shader" or ".fxh" => FileKind.Data,
        _ => FileKind.Other,
    };
}
