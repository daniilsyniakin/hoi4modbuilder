using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

public sealed partial class RecentModItemViewModel : ViewModelBase
{
    // Muted tones in the spirit of JetBrains project icons.
    private static readonly IBrush[] AvatarPalette =
        new[] { "#4A86C8", "#5C9F5C", "#B474C4", "#D49A3A", "#3D9A9A", "#C2615B", "#7C83D6" }
            .Select(hex => (IBrush)new ImmutableSolidColorBrush(Color.Parse(hex)))
            .ToArray();

    private readonly Action<RecentModItemViewModel> _open;
    private readonly Action<RecentModItemViewModel> _remove;
    private readonly Action<RecentModItemViewModel> _reveal;

    public RecentModItemViewModel(
        RecentMod mod,
        Action<RecentModItemViewModel> open,
        Action<RecentModItemViewModel> remove,
        Action<RecentModItemViewModel> reveal)
    {
        _open = open;
        _remove = remove;
        _reveal = reveal;
        Name = mod.Name;
        Path = mod.Path;
        IsMissing = !Directory.Exists(mod.Path);
        Initials = MakeInitials(mod.Name);
        AvatarBrush = AvatarPalette[StableHash(mod.Name) % (uint)AvatarPalette.Length];
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsMissing { get; }
    public string Initials { get; }
    public IBrush AvatarBrush { get; }

    public string DisplayPath => Paths.ForDisplay(Path);

    public bool Matches(string filter) =>
        Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Path.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    [RelayCommand]
    private void Open() => _open(this);

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private void Reveal() => _reveal(this);

    private static string MakeInitials(string name)
    {
        var words = name.Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .ToArray();
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{words[0][0]}{words[1][0]}".ToUpperInvariant(),
        };
    }

    // string.GetHashCode is randomized per process; avatar colours must stay the same between runs.
    private static uint StableHash(string text)
    {
        uint hash = 17;
        foreach (var c in text)
            hash = hash * 31 + c;
        return hash;
    }
}
