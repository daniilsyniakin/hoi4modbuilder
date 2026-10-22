namespace HoI4ModBuilder.Models;

public sealed class AppSettings
{
    public string? GamePath { get; set; }
    public string? ModsDirectory { get; set; }

    /// <summary>Localisation shown in the graphical editors (game folder name); null shows ids.</summary>
    public string? Language { get; set; } = "english";
    public List<RecentMod> RecentMods { get; set; } = [];
}

public sealed class RecentMod
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public DateTime LastOpened { get; set; }
}
