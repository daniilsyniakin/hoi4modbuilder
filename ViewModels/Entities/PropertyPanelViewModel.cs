using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Game.Sprites;
using HoI4ModBuilder.Game.Tooltips;
using HoI4ModBuilder.Game.Validation;
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.ViewModels.Entities;

/// <summary>
/// The inspector on the right of a graphical editor: picture, name and description as the game
/// shows them, then every property — present or possible — as an editable row.
/// </summary>
public sealed partial class PropertyPanelViewModel : ViewModelBase
{
    public PropertyPanelViewModel(string identity, string title, string subtitle, Action? openInCode)
    {
        Identity = identity;
        Title = title;
        Subtitle = subtitle;
        OpenInCodeCommand = new RelayCommand(() => openInCode?.Invoke(), () => openInCode is not null);
    }

    /// <summary>What the panel describes, e.g. "Focus:UKR_army"; a refresh of the same object reuses the panel.</summary>
    public string Identity { get; }

    /// <summary>Localised name, or the id when there is none.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>What it is and its id, e.g. "Фокус · UKR_army".</summary>
    [ObservableProperty]
    public partial string Subtitle { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    /// <summary>The localised name, edited in place; null when texts can't be edited (no language, no key).</summary>
    [ObservableProperty]
    public partial LocalisedTextViewModel? TitleText { get; set; }

    [ObservableProperty]
    public partial LocalisedTextViewModel? DescriptionText { get; set; }

    /// <summary>Sprite or texture the picture shows; the image is loaded from it.</summary>
    public string? PictureName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPicture), nameof(ShowStaticPicture))]
    public partial IImage? Picture { get; set; }

    /// <summary>Set when the picture can be changed by clicking it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPicture), nameof(CanEditPicture), nameof(ShowStaticPicture))]
    public partial PictureEditor? PictureEditor { get; set; }

    public bool CanEditPicture => PictureEditor is not null;

    /// <summary>Editable pictures show a placeholder even before one is chosen.</summary>
    public bool ShowPicture => Picture is not null || PictureEditor is not null;

    /// <summary>A picture that can't be changed here (e.g. a portrait); nothing at all when there is none.</summary>
    public bool ShowStaticPicture => Picture is not null && PictureEditor is null;

    /// <summary>Problems of the object as a whole, listed under its name.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Problem> Problems { get; set; } = [];

    /// <summary>What the game's tooltip of the object says: its modifiers and effects in words.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TooltipLine>? Tooltip { get; set; }

    /// <summary>The entity's own block: its rows and the "add property" box.</summary>
    [ObservableProperty]
    public partial BlockNode? Root { get; set; }

    public IRelayCommand OpenInCodeCommand { get; }

    /// <summary>Takes over a newer build of the same object, keeping the rows the user is working in.</summary>
    public void UpdateFrom(PropertyPanelViewModel newer)
    {
        Title = newer.Title;
        Subtitle = newer.Subtitle;
        Description = newer.Description;
        Problems = newer.Problems;
        Tooltip = newer.Tooltip;
        TitleText = LocalisedTextViewModel.Merge(TitleText, newer.TitleText);
        DescriptionText = LocalisedTextViewModel.Merge(DescriptionText, newer.DescriptionText);
        PictureEditor = newer.PictureEditor;
        if (newer.PictureName != PictureName)
        {
            PictureName = newer.PictureName;
            Picture = newer.Picture;
        }
        if (Root is not null && newer.Root is not null)
            Root.UpdateFrom(newer.Root);
        else
            Root = newer.Root;
    }
}

/// <summary>A localised text (name, description) edited in place in the chosen language.</summary>
public sealed partial class LocalisedTextViewModel : ViewModelBase
{
    private readonly Action<string> _save;
    private string _committed;

    /// <param name="text">The current text in its editable form; empty when there is none yet.</param>
    /// <param name="placeholder">Shown while there is no text, e.g. the id.</param>
    public LocalisedTextViewModel(string key, string text, string placeholder, Action<string> save)
    {
        Key = key;
        Placeholder = placeholder;
        _save = save;
        _committed = text;
        Text = text;
    }

    /// <summary>The localisation key the text is stored under.</summary>
    public string Key { get; }

    public string Placeholder { get; }

    [ObservableProperty]
    public partial string Text { get; set; }

    [RelayCommand]
    public void Commit()
    {
        if (Text == _committed)
            return;
        _committed = Text;
        _save(Text);
    }

    [RelayCommand]
    public void Revert() => Text = _committed;

    /// <summary>Keeps the existing object (and what is being typed into it) when it is for the same key.</summary>
    public static LocalisedTextViewModel? Merge(LocalisedTextViewModel? current, LocalisedTextViewModel? newer)
    {
        if (current is null || newer is null || current.Key != newer.Key)
            return newer;
        if (current.Text == current._committed)
            current.Text = newer._committed;
        current._committed = newer._committed;
        return current;
    }
}

/// <summary>Changing a picture by choosing a sprite from the game and the mod, or importing a picture file.</summary>
/// <param name="prefixes">Sprite name prefixes that fit, e.g. <c>GFX_goal_</c> for focuses.</param>
/// <param name="apply">Writes the chosen sprite (full <c>GFX_…</c> name) into the script.</param>
/// <param name="import">Imports a picture file as a new sprite and applies it; false if it failed (already reported).</param>
public sealed class PictureEditor(GameAssets assets, IReadOnlyList<string> prefixes, Action<string> apply, Func<string, bool>? import = null)
{
    private SpritePickerViewModel? _picker;

    /// <summary>Built on first use: listing thousands of sprites isn't free.</summary>
    public SpritePickerViewModel Picker => _picker ??= new SpritePickerViewModel(assets, prefixes, apply, import);

    public bool CanImport => import is not null;

    /// <summary>Imports a dropped or chosen picture file.</summary>
    public bool Import(string path) => import?.Invoke(path) ?? false;
}

/// <summary>A searchable list of sprites; thumbnails load only for rows that scroll into view.</summary>
public sealed partial class SpritePickerViewModel : ViewModelBase
{
    private const int MaxShown = 400;

    private readonly List<SpriteChoiceViewModel> _all;
    private readonly Action<string> _apply;

    private readonly Func<string, bool>? _import;

    public SpritePickerViewModel(GameAssets assets, IReadOnlyList<string> prefixes, Action<string> apply, Func<string, bool>? import = null)
    {
        _apply = apply;
        _import = import;
        _all = assets.Sprites.All
            .Where(s => prefixes.Any(p => s.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            // "_shine" sprites are the animated glow drawn over an icon, not icons of their own.
            .Where(s => !s.Name.EndsWith("_shine", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SpriteChoiceViewModel(s, assets, prefixes))
            .ToList();
        Filter();
    }

    [ObservableProperty]
    public partial string? Search { get; set; }

    public ObservableCollection<SpriteChoiceViewModel> Shown { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    partial void OnSearchChanged(string? value) => Filter();

    public void Choose(SpriteChoiceViewModel choice) => _apply(choice.Name);

    /// <summary>Pictures of this kind can be imported from files.</summary>
    public bool CanImport => _import is not null;

    public bool Import(string path) => _import?.Invoke(path) ?? false;

    private void Filter()
    {
        var words = (Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _all.Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        Shown.Clear();
        foreach (var choice in matches.Take(MaxShown))
            Shown.Add(choice);
        Summary = matches.Count > MaxShown
            ? $"Показано {MaxShown} з {matches.Count} — уточніть пошук"
            : $"Знайдено: {matches.Count}";
    }
}

public sealed partial class SpriteChoiceViewModel(SpriteDefinition sprite, GameAssets assets, IReadOnlyList<string> prefixes) : ViewModelBase
{
    private IImage? _image;
    private bool _requested;

    public string Name => sprite.Name;

    /// <summary>The name without the common prefix: "generic_army_doctrines".</summary>
    public string ShortName { get; } =
        prefixes.FirstOrDefault(p => sprite.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) is { } prefix
            ? sprite.Name[prefix.Length..]
            : sprite.Name;

    /// <summary>Loaded the first time the row asks for it, i.e. when it becomes visible.</summary>
    public IImage? Image
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _image;
        }
    }

    private async Task LoadAsync()
    {
        _image = (await assets.LoadSpriteAsync(sprite))?.Image;
        OnPropertyChanged(nameof(Image));
    }
}
