using Avalonia.Controls;
using Avalonia.Input;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Views;

public partial class MapView : UserControl
{
    public MapView()
    {
        InitializeComponent();
        Surface.BackgroundClicked += (_, e) => ViewModel?.SelectAt(e.World);
        Surface.AddHandler(PointerMovedEvent, (_, e) => ViewModel?.HoverAt(Surface.ToWorld(e.GetPosition(Surface))),
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Surface.PointerExited += (_, _) => ViewModel?.ClearHover();
        Surface.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && ViewModel is { IsPicking: true } viewModel)
            {
                viewModel.CancelPick();
                e.Handled = true;
            }
        };
    }

    private MapTabViewModel? ViewModel => DataContext as MapTabViewModel;
}
