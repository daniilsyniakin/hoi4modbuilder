using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.ViewModels;

/// <summary>One place an id is written: in which entity, where inside it, and the line.</summary>
public sealed class UsageRowViewModel(Usage usage, string owner, IRelayCommand open, IRelayCommand openCode)
{
    public Usage Usage { get; } = usage;

    public ContentKind? OwnerKind => Usage.Owner?.Kind;

    public bool HasOwner => Usage.Owner is not null;

    /// <summary>The entity's name (or id), or the localisation key.</summary>
    public string Owner { get; } = owner;

    public string Context => Usage.Owner is null ? "" : Usage.Context;

    public string Snippet => Usage.Snippet;

    public string LineText => $"рядок {Usage.Line}";

    public bool IsDefinition => Usage.IsDefinition;

    /// <summary>The owner in its graphical editor, or the line when there is none.</summary>
    public IRelayCommand Open { get; } = open;

    public IRelayCommand OpenCode { get; } = openCode;
}

public sealed class UsageFileViewModel(string file, IReadOnlyList<UsageRowViewModel> rows)
{
    /// <summary>Relative to the mod: <c>common/national_focus/ukraine.txt</c>.</summary>
    public string File { get; } = file;

    public IReadOnlyList<UsageRowViewModel> Rows { get; } = rows;

    public int Count => Rows.Count;
}

/// <summary>The results of "find usages": where the last searched id is written, by file.</summary>
public sealed partial class UsagesViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial string? Target { get; private set; }

    public bool HasTarget => Target is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<UsageFileViewModel> Files { get; private set; } = [];

    [ObservableProperty]
    public partial int Count { get; private set; }

    public bool IsEmpty => Files.Count == 0;

    public void Show(string target, IReadOnlyList<UsageFileViewModel> files)
    {
        Target = target;
        Files = files;
        Count = files.Sum(f => f.Count);
    }
}
