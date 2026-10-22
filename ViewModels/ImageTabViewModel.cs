using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

/// <summary>A texture (DDS, TGA, PNG…) opened for viewing. Follows changes on disk like text tabs.</summary>
public sealed partial class ImageTabViewModel : DocumentTabViewModel
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 32;
    private const double ZoomStep = 1.25;

    private readonly TextureCache _textures;
    private int _loadGeneration;

    public ImageTabViewModel(string filePath, string rootPath, Action<DocumentTabViewModel> close, TextureCache textures)
        : base(filePath, rootPath, close)
    {
        _textures = textures;
        _ = LoadAsync();
    }

    [ObservableProperty]
    public partial IImage? Image { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight), nameof(Info))]
    public partial int PixelWidth { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight), nameof(Info))]
    public partial int PixelHeight { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Info))]
    public partial string? Format { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight), nameof(ZoomText), nameof(Interpolation))]
    public partial double Zoom { get; set; } = 1;

    /// <summary>When set, the view keeps the image fitted to the available space.</summary>
    [ObservableProperty]
    public partial bool FitToWindow { get; set; } = true;

    public double DisplayWidth => PixelWidth * Zoom;

    public double DisplayHeight => PixelHeight * Zoom;

    public string ZoomText => $"{Zoom * 100:0}%";

    /// <summary>Crisp pixels when magnified (icons are small), smooth when shrunk.</summary>
    public BitmapInterpolationMode Interpolation =>
        Zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;

    public string Info
    {
        get
        {
            if (PixelWidth == 0)
                return "";
            var size = new FileInfo(FilePath) is { Exists: true } file ? $" · {FormatSize(file.Length)}" : "";
            return $"{PixelWidth} × {PixelHeight} · {Format}{size}";
        }
    }

    [RelayCommand]
    private void ZoomIn() => SetZoom(Zoom * ZoomStep);

    [RelayCommand]
    private void ZoomOut() => SetZoom(Zoom / ZoomStep);

    [RelayCommand]
    private void ActualSize() => SetZoom(1);

    [RelayCommand]
    private void Fit() => FitToWindow = true;

    /// <summary>Manual zoom ends fit-to-window mode.</summary>
    public void SetZoom(double zoom)
    {
        FitToWindow = false;
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    /// <summary>Called by the view with the space available for the image.</summary>
    public void UpdateFit(double availableWidth, double availableHeight)
    {
        if (!FitToWindow || PixelWidth == 0 || availableWidth <= 0 || availableHeight <= 0)
            return;
        // Never blow small icons up beyond 8× when fitting; that just looks blurry-big.
        Zoom = Math.Clamp(Math.Min(availableWidth / PixelWidth, availableHeight / PixelHeight), MinZoom, 8);
    }

    public override void ReloadFromDisk()
    {
        _textures.Invalidate(FilePath);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        IsLoading = true;
        var result = await _textures.GetAsync(FilePath);
        if (generation != _loadGeneration)
            return;

        IsLoading = false;
        Error = result.Error;
        Image = result.Image;
        Format = result.Format;
        PixelWidth = result.Width;
        PixelHeight = result.Height;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} МБ",
    };
}
