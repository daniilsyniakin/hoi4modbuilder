using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Sprites;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.Views;

/// <summary>
/// Tooltip content previewing a sprite or texture. Built only after the image is decoded:
/// tooltip popups size themselves once, so content must not grow after opening.
/// </summary>
public static class AssetPreview
{
    private const double MaxSize = 160;

    public static async Task<Control> ForSpriteAsync(GameAssets assets, SpriteDefinition sprite)
    {
        var result = await assets.LoadSpriteAsync(sprite);
        if (result is null)
        {
            return Layout(null, sprite.Name,
                $"Текстуру «{sprite.TextureFile}» не знайдено\nВизначено в {sprite.SourceFile} {LayerLabel(sprite.Layer)}");
        }

        var frames = sprite.Frames > 1 ? $" · кадрів: {sprite.Frames}" : "";
        return Layout(result.Image, sprite.Name,
            $"{result.Width} × {result.Height}{frames} · {result.Texture.Format}\n" +
            $"{sprite.TextureFile} {LayerLabel(result.File.Layer)}\n" +
            $"Визначено в {sprite.SourceFile} {LayerLabel(sprite.Layer)}");
    }

    public static async Task<Control> ForTextureAsync(GameAssets assets, VirtualFile file)
    {
        var result = await assets.Textures.GetAsync(file.FullPath);
        var title = Path.GetFileName(file.RelativePath);
        return result.Image is null
            ? Layout(null, title, result.Error ?? "Не вдалося відкрити")
            : Layout(result.Image, title, $"{result.Width} × {result.Height} · {result.Format}\n{file.RelativePath} {LayerLabel(file.Layer)}");
    }

    private static string LayerLabel(FileLayer layer) => layer switch
    {
        FileLayer.Mod => "(мод)",
        FileLayer.Dlc => "(DLC)",
        _ => "(гра)",
    };

    /// <summary>[image] beside the title and detail lines.</summary>
    private static Control Layout(IImage? source, string title, string details)
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (source is not null)
        {
            var image = new Image
            {
                Source = source,
                MaxWidth = MaxSize,
                MaxHeight = MaxSize,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0),
            };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
            layout.Children.Add(image);
        }

        var text = new StackPanel { Spacing = 4, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var line in details.Split('\n'))
            text.Children.Add(new TextBlock { Text = line, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);
        layout.Children.Add(text);
        return layout;
    }
}
