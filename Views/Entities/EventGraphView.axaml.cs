using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HoI4ModBuilder.ViewModels.Entities;

namespace HoI4ModBuilder.Views.Entities;

public partial class EventGraphView : UserControl
{
    public EventGraphView()
    {
        InitializeComponent();
        World.AddHandler(PointerPressedEvent, OnItemPressed, RoutingStrategies.Bubble);
        World.AddHandler(DoubleTappedEvent, OnItemDoubleTapped, RoutingStrategies.Bubble);
        Surface.BackgroundClicked += (_, _) =>
        {
            if (ViewModel is { } viewModel)
                viewModel.Selection = null;
        };
        Surface.ZoomChanged += (_, _) => UpdateZoomText();
        UpdateZoomText();
    }

    private EventGraphTabViewModel? ViewModel => DataContext as EventGraphTabViewModel;

    /// <summary>An option row, an event card or a source card under the pointer.</summary>
    private static object? ItemOf(RoutedEventArgs e) =>
        (e.Source as StyledElement)?.DataContext is var item and (EventOptionViewModel or GraphNodeViewModel) ? item : null;

    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } viewModel || ItemOf(e) is not { } item)
            return;
        var point = e.GetCurrentPoint(World);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed)
            return;
        viewModel.Selection = item;
        // Right click goes on to open the context menu; left click must not start a pan.
        if (point.Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            Surface.Focus();
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ItemOf(e) is not { } item)
            return;
        e.Handled = true;
        ViewModel?.Open(item is EventOptionViewModel option ? option.Owner : item);
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1.25);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1 / 1.25);

    private void OnFit(object? sender, RoutedEventArgs e) => Surface.Fit();

    private void UpdateZoomText() => ZoomText.Text = $"{Surface.Zoom * 100:0}%";
}
