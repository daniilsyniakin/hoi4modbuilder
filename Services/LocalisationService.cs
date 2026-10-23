using Avalonia.Threading;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Localisation;

namespace HoI4ModBuilder.Services;

/// <param name="Code">The game's folder and file-suffix name, e.g. <c>english</c>; null shows raw ids.</param>
/// <param name="Short">Shown in the status bar: EN, RU…</param>
public sealed record LocalisationLanguage(string? Code, string Title, string Short);

/// <summary>Game and mod texts (focus names, event titles…), reloaded when the mod's .yml files change.</summary>
public sealed class LocalisationService
{
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(500);

    private readonly GameFileSystem _fileSystem;
    private readonly DispatcherTimer _reloadTimer;

    /// <summary>The languages Hearts of Iron IV ships, plus "none" to see the ids themselves.</summary>
    public static IReadOnlyList<LocalisationLanguage> Languages { get; } =
    [
        new(null, "Без локалізації (id)", "ID"),
        new("english", "English", "EN"),
        new("russian", "Русский", "RU"),
        new("german", "Deutsch", "DE"),
        new("french", "Français", "FR"),
        new("polish", "Polski", "PL"),
        new("spanish", "Español", "ES"),
        new("braz_por", "Português (Brasil)", "PT"),
        new("japanese", "日本語", "JA"),
        new("korean", "한국어", "KO"),
        new("simp_chinese", "简体中文", "ZH"),
    ];

    public LocalisationService(GameFileSystem fileSystem, string? language)
    {
        _fileSystem = fileSystem;
        Language = language;
        _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
        _reloadTimer.Tick += async (_, _) =>
        {
            _reloadTimer.Stop();
            await LoadAsync();
        };
    }

    public LocalisationIndex Index { get; private set; } = LocalisationIndex.Empty;

    /// <summary>The language names are shown in; null shows ids instead of names.</summary>
    public string? Language { get; private set; }

    public Task SetLanguageAsync(string? language)
    {
        Language = language;
        return LoadAsync();
    }

    /// <summary>
    /// Ids instead of names everywhere names are shown, while Right Alt is held. Only <see cref="Text"/>
    /// follows it: editing and checking texts goes through <see cref="Index"/>.
    /// </summary>
    public bool ShowIds
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised on the UI thread whenever <see cref="Index"/> is replaced or edited.</summary>
    public event EventHandler? IndexChanged;

    /// <summary>Raised on the UI thread whenever shown names may differ: the index changed or <see cref="ShowIds"/> did.</summary>
    public event EventHandler? Changed;

    private void OnIndexChanged()
    {
        IndexChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadAsync()
    {
        var language = Language;
        var index = language is null
            ? LocalisationIndex.Empty
            : await Task.Run(() => LocalisationIndex.Load(_fileSystem, language));
        if (language != Language)
            return; // switched again while loading
        Index = index;
        OnIndexChanged();
    }

    public void OnFileChanged(string fullPath)
    {
        if (!Path.GetExtension(fullPath).Equals(".yml", StringComparison.OrdinalIgnoreCase))
            return;
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    /// <summary>Shows a just-written text at once, before the files are re-read.</summary>
    public void Apply(string key, string storedValue)
    {
        Index.Set(key, storedValue);
        OnIndexChanged();
    }

    /// <summary>The display text for <paramref name="key"/>, or null when there is none.</summary>
    public string? Text(string? key) => ShowIds ? null : Index.FindDisplay(key);

    /// <summary>What the game calls a mod entity; null when it has no name in this language.</summary>
    public string? TitleOf(ContentEntry entry) => entry.Kind switch
    {
        ContentKind.State => Text(entry.Detail ?? $"STATE_{entry.Id}"),
        ContentKind.Event => Text(entry.Id) ?? Text(entry.Id + ".t"),
        // Their ids are names only for the script; a localisation key with the same name would be a coincidence.
        ContentKind.ScriptedEffect or ContentKind.ScriptedTrigger => null,
        _ => Text(entry.Id),
    };
}
