using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HoI4ModBuilder.Game.Validation;

namespace HoI4ModBuilder.Views.Converters;

/// <summary>Icon and colour of a problem severity (from Styles/Theme.axaml).</summary>
public static class ProblemIcons
{
    public static readonly IValueConverter Geometry = new FuncValueConverter<ProblemSeverity?, Geometry?>(severity =>
        Find<Geometry>(severity == ProblemSeverity.Error ? "IconError" : "IconWarning"));

    public static readonly IValueConverter Brush = new FuncValueConverter<ProblemSeverity?, IBrush?>(severity =>
        Find<IBrush>(severity == ProblemSeverity.Error ? "ErrorBrush" : "WarningBrush"));

    private static T? Find<T>(string key) where T : class =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            ? value as T
            : null;
}
