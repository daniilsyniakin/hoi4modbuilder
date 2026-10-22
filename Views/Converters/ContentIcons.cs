using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Views.Converters;

/// <summary>
/// Maps a <see cref="ContentKind"/> to the icon geometry and colour defined in Styles/Theme.axaml.
/// Null (a virtual folder) gets the folder icon.
/// </summary>
public static class ContentIcons
{
    public static readonly IValueConverter Geometry = new FuncValueConverter<ContentKind?, Geometry?>(kind =>
        Find<Geometry>(kind switch
        {
            null => "IconFolder",
            ContentKind.ScriptedEffect => "IconEffect",
            ContentKind.ScriptedTrigger => "IconTrigger",
            _ => $"Icon{kind}",
        }));

    public static readonly IValueConverter Brush = new FuncValueConverter<ContentKind?, IBrush?>(kind =>
        Find<IBrush>(kind is null ? "FileIconFolderBrush" : $"ContentIcon{kind}Brush"));

    private static T? Find<T>(string key) where T : class =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            ? value as T
            : null;
}
