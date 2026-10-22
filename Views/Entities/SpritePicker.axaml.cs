using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HoI4ModBuilder.ViewModels.Entities;

namespace HoI4ModBuilder.Views.Entities;

/// <summary>Search box and thumbnail list; a click (or Enter) picks the sprite.</summary>
public partial class SpritePicker : UserControl
{
    public SpritePicker()
    {
        InitializeComponent();
        List.AddHandler(TappedEvent, (_, e) => Choose(e), RoutingStrategies.Bubble);
        List.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Choose(e);
        };
        AttachedToVisualTree += (_, _) => SearchBox.Focus();
    }

    /// <summary>Raised after a sprite was applied, so the flyout can close.</summary>
    public event EventHandler? Chosen;

    private void Choose(RoutedEventArgs e)
    {
        if (DataContext is not SpritePickerViewModel picker || List.SelectedItem is not SpriteChoiceViewModel choice)
            return;
        e.Handled = true;
        picker.Choose(choice);
        Chosen?.Invoke(this, EventArgs.Empty);
    }
}
