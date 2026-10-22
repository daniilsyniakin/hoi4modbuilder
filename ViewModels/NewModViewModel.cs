using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

public sealed partial class NewModViewModel : ViewModelBase
{
    // Tags recognised by the Paradox launcher and Steam Workshop; must stay in English.
    private static readonly string[] KnownTags =
    [
        "Alternative History", "Balance", "Events", "Fixes", "Gameplay", "Graphics", "Historical",
        "Ideologies", "Map", "Military", "National Focuses", "Sound", "Technologies", "Translation", "Utilities",
    ];

    // Ukrainian national transliteration (2010), plus the extra Russian letters.
    private static readonly Dictionary<char, string> Transliteration = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "h", ['ґ'] = "g", ['д'] = "d", ['е'] = "e", ['є'] = "ie",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "y", ['і'] = "i", ['ї'] = "i", ['й'] = "i", ['к'] = "k", ['л'] = "l",
        ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ь'] = "",
        ['ю'] = "iu", ['я'] = "ia", ['\''] = "", ['’'] = "", ['ы'] = "y", ['э'] = "e", ['ё'] = "e", ['ъ'] = "",
    };

    private readonly IPlatformService _platform;
    private readonly string _launcherModsDirectory;
    private readonly Action<string> _created;
    private readonly Action _cancelled;
    private bool _folderNameEdited;
    private bool _updatingFolderName;

    public NewModViewModel(
        IPlatformService platform,
        string launcherModsDirectory,
        string supportedVersion,
        Action<string> created,
        Action cancelled)
    {
        _platform = platform;
        _launcherModsDirectory = launcherModsDirectory;
        _created = created;
        _cancelled = cancelled;
        Location = launcherModsDirectory;
        SupportedVersion = supportedVersion;
        Tags = KnownTags.Select(t => new TagOption(t)).ToArray();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPath))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string FolderName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPath))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Location { get; set; }

    [ObservableProperty]
    public partial string Version { get; set; } = "1.0";

    [ObservableProperty]
    public partial string SupportedVersion { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public IReadOnlyList<TagOption> Tags { get; }

    public string TargetPath =>
        string.IsNullOrWhiteSpace(Location) || string.IsNullOrWhiteSpace(FolderName)
            ? ""
            : Path.Combine(Location, FolderName);

    partial void OnNameChanged(string value)
    {
        if (_folderNameEdited)
            return;
        _updatingFolderName = true;
        FolderName = ToFolderName(value);
        _updatingFolderName = false;
    }

    partial void OnFolderNameChanged(string value)
    {
        // Clearing the field hands control back to the auto-generated name.
        if (!_updatingFolderName)
            _folderNameEdited = !string.IsNullOrWhiteSpace(value);
    }

    private bool CanCreate() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(FolderName)
        && !string.IsNullOrWhiteSpace(Location);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        ErrorMessage = null;
        if (FolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ErrorMessage = "Назва папки містить недопустимі символи.";
            return;
        }

        var descriptor = new ModDescriptor
        {
            Name = Name.Trim(),
            Version = string.IsNullOrWhiteSpace(Version) ? "1.0" : Version.Trim(),
            SupportedVersion = string.IsNullOrWhiteSpace(SupportedVersion) ? null : SupportedVersion.Trim(),
            Tags = Tags.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
        };

        try
        {
            _created(ModFactory.Create(descriptor, Location.Trim(), FolderName.Trim(), _launcherModsDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidModException)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancelled();

    [RelayCommand]
    private async Task BrowseLocation()
    {
        if (await _platform.PickFolderAsync("Оберіть розташування моду", Location) is { } path)
            Location = path;
    }

    /// <summary>
    /// Paradox tools are unreliable with non-ASCII paths, so the folder name is reduced to
    /// lowercase latin letters, digits and underscores (Cyrillic is transliterated).
    /// </summary>
    private static string ToFolderName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(c);
            else if (Transliteration.TryGetValue(c, out var latin))
                sb.Append(latin);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
        }
        var result = sb.ToString().TrimEnd('_');
        return result.Length > 0 || string.IsNullOrWhiteSpace(name) ? result : "new_mod";
    }
}

public sealed partial class TagOption(string name) : ViewModelBase
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
