using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Views;

public partial class ImageViewer : UserControl
{
    // Border margin and outline around the image inside the scroll viewer.
    private const double Chrome = 2 * 24 + 2;

    private ImageTabViewModel? _viewModel;

    public ImageViewer()
    {
        InitializeComponent();
        Scroller.SizeChanged += (_, _) => UpdateFit();
        Scroller.AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as ImageTabViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateFit();
        UpdateInterpolation();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageTabViewModel.FitToWindow) or nameof(ImageTabViewModel.PixelWidth))
            UpdateFit();
        if (e.PropertyName == nameof(ImageTabViewModel.Interpolation))
            UpdateInterpolation();
    }

    // RenderOptions can't be bound in XAML, so it follows the view model from here.
    private void UpdateInterpolation()
    {
        if (_viewModel is not null)
            RenderOptions.SetBitmapInterpolationMode(PreviewImage, _viewModel.Interpolation);
    }

    private void UpdateFit() =>
        _viewModel?.UpdateFit(Scroller.Bounds.Width - Chrome, Scroller.Bounds.Height - Chrome);

    /// <summary>Ctrl + wheel zooms, as in image viewers and IDE previews.</summary>
    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_viewModel is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        _viewModel.SetZoom(_viewModel.Zoom * (e.Delta.Y > 0 ? 1.25 : 1 / 1.25));
        e.Handled = true;
    }
}
