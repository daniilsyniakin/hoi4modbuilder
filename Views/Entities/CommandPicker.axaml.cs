using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.Views.Entities;

/// <summary>Effects, triggers or modifiers to add, by category or search; a click (or Enter) adds one.</summary>
public partial class CommandPicker : UserControl
{
    public CommandPicker()
    {
        InitializeComponent();
        List.AddHandler(TappedEvent, (_, e) => Choose(e), RoutingStrategies.Bubble);
        List.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Choose(e);
        };
        SearchBox.KeyDown += (_, e) =>
        {
            // Enter in the search takes the first match.
            if (e.Key == Key.Enter && List.ItemCount > 0)
            {
                List.SelectedIndex = 0;
                Choose(e);
            }
        };
        AttachedToVisualTree += (_, _) => SearchBox.Focus();
    }

    private void Choose(RoutedEventArgs e)
    {
        if (DataContext is not CommandPickerViewModel picker || List.SelectedItem is not CommandChoice choice)
            return;
        e.Handled = true;
        picker.Choose(choice);
        picker.Search = null;
        // Close the flyout this picker lives in.
        this.FindLogicalAncestorOfType<Popup>()?.Close();
    }
}
