using CommunityToolkit.Mvvm.ComponentModel;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Models;

namespace HoI4ModBuilder.ViewModels;

public enum SearchItemKind
{
    Action,
    Content,
    Localisation,
    File,
}

/// <summary>Something "search everywhere" can find and open.</summary>
/// <param name="Id">The script id, also matched (an entity's title is its localised name).</param>
public sealed record SearchItem(SearchItemKind Kind, string Title, string? Id, string? Subtitle, string Category, Action Open,
    ContentKind? ContentKind = null, FileKind? FileKind = null);

public sealed class SearchResultViewModel(SearchItem item)
{
    public SearchItem Item { get; } = item;

    public string Title => Item.Title;

    public string? Subtitle => Item.Subtitle;

    public string Category => Item.Category;

    public ContentKind? ContentKind => Item.ContentKind;

    public bool IsContent => Item.Kind == SearchItemKind.Content;

    public FileKind FileKind => Item.FileKind ?? default;

    public bool IsFile => Item.Kind == SearchItemKind.File;

    public bool IsLocalisation => Item.Kind == SearchItemKind.Localisation;

    public bool IsAction => Item.Kind == SearchItemKind.Action;
}

/// <summary>
/// Search everywhere (double Shift): the mod's entities by id or name, localisation keys and texts,
/// files and the app's own actions in one list, best matches first.
/// </summary>
public sealed partial class SearchEverywhereViewModel(Func<IReadOnlyList<SearchItem>> collect) : ViewModelBase
{
    private const int MaxResults = 80;

    private IReadOnlyList<SearchItem> _items = [];

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string? Query { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial IReadOnlyList<SearchResultViewModel> Results { get; private set; } = [];

    public bool HasResults => Results.Count > 0;

    [ObservableProperty]
    public partial SearchResultViewModel? Selected { get; set; }

    partial void OnQueryChanged(string? value) => Filter();

    public void Open()
    {
        _items = collect();
        Query = "";
        IsOpen = true;
        Filter();
    }

    public void Close()
    {
        IsOpen = false;
        _items = [];
        Results = [];
    }

    public void Move(int delta)
    {
        if (Results.Count == 0)
            return;
        var index = Selected is null ? -1 : Results.ToList().IndexOf(Selected);
        Selected = Results[Math.Clamp(index + delta, 0, Results.Count - 1)];
    }

    /// <summary>Enter or a click: opens the result.</summary>
    public void Commit(SearchResultViewModel? result = null)
    {
        result ??= Selected;
        Close();
        result?.Item.Open();
    }

    private void Filter()
    {
        var words = (Query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            Results = [];
            Selected = null;
            return;
        }
        Results = _items
            .Select(item => (Item: item, Score: Score(item, words)))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Item.Title.Length)
            .Take(MaxResults)
            .Select(r => new SearchResultViewModel(r.Item))
            .ToList();
        Selected = Results.FirstOrDefault();
    }

    /// <summary>How well the item matches every word: exact, prefix, word start, anywhere; entities before texts and files.</summary>
    public static int Score(SearchItem item, IReadOnlyList<string> words)
    {
        var total = 0;
        foreach (var word in words)
        {
            var best = Math.Max(Score(item.Title, word), item.Id is null ? 0 : Score(item.Id, word));
            if (item.Kind == SearchItemKind.Localisation && item.Subtitle is { } text)
                best = Math.Max(best, Score(text, word) / 2);
            if (best == 0)
                return 0;
            total += best;
        }
        return total + item.Kind switch
        {
            SearchItemKind.Content => 30,
            SearchItemKind.Action => 25,
            SearchItemKind.Localisation => 10,
            _ => 0,
        };
    }

    private static int Score(string text, string word)
    {
        if (text.Equals(word, StringComparison.CurrentCultureIgnoreCase))
            return 1000;
        if (text.StartsWith(word, StringComparison.CurrentCultureIgnoreCase))
            return 800;
        var index = text.IndexOf(word, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
            return 0;
        // The start of a word inside: "industry" in "UKR_industry" or "Industrial Push".
        return index > 0 && text[index - 1] is '_' or '.' or ' ' or '-' or '/' or ':' ? 600 : 400;
    }
}
