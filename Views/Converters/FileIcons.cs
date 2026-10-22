using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HoI4ModBuilder.Models;

namespace HoI4ModBuilder.Views.Converters;

/// <summary>Maps a <see cref="FileKind"/> to the icon geometry and colour defined in Styles/Theme.axaml.</summary>
public static class FileIcons
{
    public static readonly IValueConverter Geometry = new FuncValueConverter<FileKind, Geometry?>(kind =>
        Find<Geometry>(kind switch
        {
            FileKind.Folder => "IconFolder",
            FileKind.Script => "IconScript",
            FileKind.Localisation => "IconLocalisation",
            FileKind.Interface => "IconInterface",
            FileKind.Image => "IconImage",
            FileKind.Descriptor => "IconSliders",
            FileKind.Data => "IconData",
            _ => "IconFile",
        }));

    public static readonly IValueConverter Brush = new FuncValueConverter<FileKind, IBrush?>(kind =>
        Find<IBrush>($"FileIcon{kind}Brush"));

    private static T? Find<T>(string key) where T : class =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            ? value as T
            : null;
}
