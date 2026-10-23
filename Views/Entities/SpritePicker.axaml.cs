using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HoI4ModBuilder.Services;
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

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpritePickerViewModel picker || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Картинка",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Зображення") { Patterns = SpriteImporter.Extensions.Select(x => "*" + x).ToList() }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && picker.Import(path))
            Chosen?.Invoke(this, EventArgs.Empty);
    }

    private void Choose(RoutedEventArgs e)
    {
        if (DataContext is not SpritePickerViewModel picker || List.SelectedItem is not SpriteChoiceViewModel choice)
            return;
        e.Handled = true;
        picker.Choose(choice);
        Chosen?.Invoke(this, EventArgs.Empty);
    }
}
