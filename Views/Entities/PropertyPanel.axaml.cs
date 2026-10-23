using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels.Entities;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.Views.Entities;

/// <summary>
/// The inspector. Values are committed on Enter, on picking a suggestion and on leaving the
/// field; Esc goes back to what the script says.
/// </summary>
public partial class PropertyPanel : UserControl
{
    public PropertyPanel()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(AutoCompleteBox.SelectionChangedEvent, OnSuggestionChosen, RoutingStrategies.Bubble);

        DragDrop.SetAllowDrop(PictureButton, true);
        PictureButton.AddHandler(DragDrop.DragOverEvent, OnPictureDragOver);
        PictureButton.AddHandler(DragDrop.DragLeaveEvent, (_, _) => PictureButton.Classes.Set("drop", false));
        PictureButton.AddHandler(DragDrop.DropEvent, OnPictureDrop);

        // The picker lists thousands of sprites, so it is only built when the flyout opens.
        var flyout = (Flyout)PictureButton.Flyout!;
        flyout.Opening += (_, _) => Picker.DataContext = (DataContext as PropertyPanelViewModel)?.PictureEditor?.Picker;
        Picker.Chosen += (_, _) => flyout.Hide();
    }

    /// <summary>The first dragged file that is a picture, if any.</summary>
    private static string? DraggedPicture(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?
            .Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => p is not null && SpriteImporter.CanImport(p));

    private void OnPictureDragOver(object? sender, DragEventArgs e)
    {
        var accepts = DataContext is PropertyPanelViewModel { PictureEditor.CanImport: true } && DraggedPicture(e) is not null;
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        PictureButton.Classes.Set("drop", accepts);
        e.Handled = true;
    }

    private void OnPictureDrop(object? sender, DragEventArgs e)
    {
        PictureButton.Classes.Set("drop", false);
        if (DataContext is PropertyPanelViewModel { PictureEditor: { } editor } && DraggedPicture(e) is { } path)
            editor.Import(path);
        e.Handled = true;
    }

    private static AutoCompleteBox? AutoCompleteOf(RoutedEventArgs e) =>
        e.Source as AutoCompleteBox ?? (e.Source as Visual)?.FindAncestorOfType<AutoCompleteBox>();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox { DataContext: LocalisedTextViewModel text } textBox)
        {
            // Names: Enter saves. Descriptions take line breaks, so there Ctrl+Enter saves.
            var multiLine = textBox.AcceptsReturn;
            if (e.Key == Key.Enter && (!multiLine || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                text.Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                text.Revert();
                e.Handled = true;
            }
            return;
        }

        if (e.Key is not (Key.Enter or Key.Escape) || (e.Source as Control)?.DataContext is not { } context)
            return;
        var box = AutoCompleteOf(e);
        // Let the box take a highlighted suggestion first, then act on the final text.
        Dispatcher.UIThread.Post(() =>
        {
            switch (context)
            {
                case ScalarNode node when e.Key == Key.Enter:
                    node.Commit();
                    break;
                case ScalarNode node:
                    node.Revert();
                    break;
                case BlockNode block when e.Key == Key.Enter && box is not null:
                    Add(block, box);
                    break;
            }
        });
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: LocalisedTextViewModel text })
        {
            text.Commit();
            return;
        }
        // Focus moving inside the same box (e.g. into its drop-down) isn't leaving it.
        if ((e.Source as Control)?.DataContext is ScalarNode node && !IsFocusWithin(AutoCompleteOf(e)))
            node.Commit();
    }

    private static bool IsFocusWithin(Control? control) => control?.IsKeyboardFocusWithin == true;

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        // Show the choices straight away: picking beats typing.
        if (AutoCompleteOf(e) is not { } box || box.IsDropDownOpen)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (box.IsKeyboardFocusWithin && box.ItemsSource?.Cast<object>().Any() == true)
                box.IsDropDownOpen = true;
        });
    }

    private void OnSuggestionChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (AutoCompleteOf(e) is not { IsDropDownOpen: false } box || e.AddedItems is not [string])
            return;
        Dispatcher.UIThread.Post(() =>
        {
            switch (box.DataContext)
            {
                case ScalarNode node:
                    node.Commit();
                    break;
                case BlockNode block:
                    Add(block, box);
                    break;
            }
        });
    }

    private static void Add(BlockNode block, AutoCompleteBox box)
    {
        if (box.Classes.Contains("add-item"))
            block.AddItem(box.Text);
        else if (box.Classes.Contains("add-key"))
            block.AddProperty(box.Text);
        box.Text = "";
    }
}
