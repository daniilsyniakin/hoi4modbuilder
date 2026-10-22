using Avalonia.Controls;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.Views.Entities;

/// <summary>
/// An auto-complete box for picking script values: shows every suggestion while the text is empty
/// or still the current value, and the ones containing the text once the user types.
/// </summary>
public class SuggestionBox : AutoCompleteBox
{
    public SuggestionBox()
    {
        FilterMode = AutoCompleteFilterMode.Custom;
        MinimumPrefixLength = 0;
        ItemFilter = Matches;
    }

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    private bool Matches(string? search, object? item)
    {
        if (string.IsNullOrEmpty(search) || item is not string text)
            return true;
        if (DataContext is ScalarNode node && search == node.Text && ItemsSource?.Cast<object>().Contains(search) == true)
            return true;
        return text.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
