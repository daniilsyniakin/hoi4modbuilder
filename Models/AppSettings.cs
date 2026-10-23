namespace HoI4ModBuilder.Models;

public sealed class AppSettings
{
    public string? GamePath { get; set; }
    public string? ModsDirectory { get; set; }

    /// <summary>Localisation shown in the graphical editors (game folder name); null shows ids.</summary>
    public string? Language { get; set; } = "english";
    public List<RecentMod> RecentMods { get; set; } = [];

    /// <summary>Where the mod window was and how big, shared by all mods.</summary>
    public WindowLayout? Window { get; set; }

    /// <summary>What was open in each mod, by its normalized lower-case path.</summary>
    public Dictionary<string, ModSession> Sessions { get; set; } = [];
}

public sealed class WindowLayout
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public double ProjectPanelWidth { get; set; }
}

public sealed class ModSession
{
    public bool ContentMode { get; set; }
    public bool ProjectPanelVisible { get; set; } = true;
    public bool BottomPanelVisible { get; set; }

    /// <summary>"Problems" or "GameLog".</summary>
    public string? BottomTab { get; set; }
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveTab { get; set; } = -1;
}

/// <summary>A file (by path, with the caret), a graphical editor (by kind and id) or a database tab.</summary>
public sealed class SessionTab
{
    public string? Path { get; set; }
    public int Caret { get; set; }
    public string? Kind { get; set; }
    public string? Id { get; set; }

    /// <summary>A database tab: "localisation" or "technologies".</summary>
    public string? Tool { get; set; }
}

public sealed class RecentMod
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public DateTime LastOpened { get; set; }
}
