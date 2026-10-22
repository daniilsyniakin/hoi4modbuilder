using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HoI4ModBuilder.ViewModels.Entities.Inspector;

/// <summary>Choosing an effect, trigger or modifier to add: by category or by search, most used first.</summary>
public sealed partial class CommandPickerViewModel : ViewModelBase
{
    public const string AllCategories = "Усі";
    private const int MaxShown = 300;

    private readonly IReadOnlyList<CommandChoice> _all;
    private readonly Action<CommandChoice> _apply;

    public CommandPickerViewModel(IReadOnlyList<CommandChoice> all, IEnumerable<string> categories, Action<CommandChoice> apply)
    {
        _all = all;
        _apply = apply;
        // Only categories that have something in them.
        Categories = [AllCategories, .. categories.Where(c => all.Any(a => a.Category == c))];
        SelectedCategory = AllCategories;
    }

    public IReadOnlyList<string> Categories { get; }

    [ObservableProperty]
    public partial string SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string? Search { get; set; }

    public ObservableCollection<CommandChoice> Shown { get; } = [];

    partial void OnSelectedCategoryChanged(string value) => Filter();

    partial void OnSearchChanged(string? value) => Filter();

    public void Choose(CommandChoice choice) => _apply(choice);

    private void Filter()
    {
        var words = (Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Searching looks through every category.
        var category = words.Length > 0 ? AllCategories : SelectedCategory;
        var matches = _all
            .Where(c => category == AllCategories || c.Category == category)
            .Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                       || c.Description.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxShown);
        Shown.Clear();
        foreach (var choice in matches)
            Shown.Add(choice);
    }
}
