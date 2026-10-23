using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Views;

public partial class TechnologyDatabaseView : UserControl
{
    public TechnologyDatabaseView()
    {
        InitializeComponent();
        Table.DoubleTapped += (_, _) => Open();
        Table.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Open();
        };
    }

    private void Open() =>
        (DataContext as TechnologyDatabaseTabViewModel)?.OpenCommand.Execute(Table.SelectedItem as TechnologyRowViewModel);

    private async void OnCopyId(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TechnologyRowViewModel row && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(row.Id);
    }
}
