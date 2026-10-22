using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Validation;

namespace HoI4ModBuilder.ViewModels;

/// <summary>
/// A node of the "Контент" view: a category (Події), a virtual folder inside it (an event
/// namespace) or an entity defined somewhere in the mod's files.
/// </summary>
public sealed partial class ContentNodeViewModel : ViewModelBase
{
    public ContentNodeViewModel(string key, string name, ContentKind? kind)
    {
        Key = key;
        Name = name;
        Kind = kind;
    }

    /// <summary>Identifies the node among its siblings across rebuilds, so expansion and selection survive.</summary>
    public string Key { get; }

    public string Name { get; }

    /// <summary>The entity kind (or the kind a category holds); null for virtual folders.</summary>
    public ContentKind? Kind { get; }

    public ObservableCollection<ContentNodeViewModel> Children { get; } = [];

    /// <summary>Secondary text: an entry count for folders, a hint for entities.</summary>
    [ObservableProperty]
    public partial string? Description { get; set; }

    /// <summary>Where the entity is defined; null for categories and folders.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocation))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    public partial int Position { get; set; }

    [ObservableProperty]
    public partial string? ToolTip { get; set; }

    /// <summary>Categories with nothing in them are dimmed.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool HasLocation => FilePath is not null;

    /// <summary>The worst problem in the entity, to colour its name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(HasWarning))]
    public partial ProblemSeverity? Severity { get; set; }

    public bool HasError => Severity == ProblemSeverity.Error;

    public bool HasWarning => Severity == ProblemSeverity.Warning;

    /// <summary>
    /// The group this node is or belongs to (event namespace, idea slot, decision category) and
    /// the kind it groups — where something new goes when this node is selected.
    /// </summary>
    public string? Group { get; set; }

    public ContentKind? GroupKind { get; set; }

    /// <summary>For a focus inside a tree: the tree's id, so opening the focus shows it in its tree.</summary>
    public string? OwnerId { get; set; }

    /// <summary>All nodes from this one down.</summary>
    public IEnumerable<ContentNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        foreach (var node in child.SelfAndDescendants())
            yield return node;
    }

    /// <summary>
    /// Makes <paramref name="target"/> look like <paramref name="source"/>, reusing nodes with the
    /// same key so that the tree keeps its expansion, selection and scroll position.
    /// </summary>
    public static void Sync(ObservableCollection<ContentNodeViewModel> target, IReadOnlyList<ContentNodeViewModel> source)
    {
        var existing = target.ToDictionary(n => n.Key);
        var result = source.Select(s => existing.Remove(s.Key, out var old) ? old.CopyFrom(s) : s).ToList();

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (existing.ContainsKey(target[i].Key))
                target.RemoveAt(i);
        }

        for (var i = 0; i < result.Count; i++)
        {
            if (i < target.Count && target[i] == result[i])
                continue;
            var current = target.IndexOf(result[i]);
            if (current >= 0)
                target.Move(current, i);
            else
                target.Insert(i, result[i]);
        }
    }

    private ContentNodeViewModel CopyFrom(ContentNodeViewModel source)
    {
        Description = source.Description;
        FilePath = source.FilePath;
        Position = source.Position;
        ToolTip = source.ToolTip;
        IsEmpty = source.IsEmpty;
        OwnerId = source.OwnerId;
        Severity = source.Severity;
        Group = source.Group;
        GroupKind = source.GroupKind;
        Sync(Children, source.Children);
        return this;
    }
}
