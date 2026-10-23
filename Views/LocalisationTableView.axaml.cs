using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Views;

public partial class LocalisationTableView : UserControl
{
    public LocalisationTableView()
    {
        InitializeComponent();
        // Cells write when left or on Enter (Shift+Enter is a line break); Esc takes the edit back.
        AddHandler(LostFocusEvent, (_, e) => CellOf(e)?.Commit(), RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (CellOf(e) is not { } cell)
            return;
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                cell.Commit();
                e.Handled = true;
                break;
            case Key.Escape:
                cell.Revert();
                e.Handled = true;
                break;
        }
    }

    private static LocalisationCellViewModel? CellOf(RoutedEventArgs e) =>
        e.Source is Control { DataContext: LocalisationCellViewModel cell } control && control.FindAncestorOfType<TextBox>(includeSelf: true) is not null
            ? cell
            : null;
}
