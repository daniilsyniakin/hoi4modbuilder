using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HoI4ModBuilder.Game.Tooltips;

namespace HoI4ModBuilder.Views;

/// <summary>The game's text colours, a little toned down for the dark theme.</summary>
public static class GameColours
{
    private static readonly Dictionary<char, IBrush> Brushes = new()
    {
        ['Y'] = Brush("#F2C14E"),
        ['H'] = Brush("#F2C14E"),
        ['G'] = Brush("#62D26F"),
        ['R'] = Brush("#F26363"),
        ['B'] = Brush("#5EB0F2"),
        ['C'] = Brush("#5FD4E0"),
        ['O'] = Brush("#F2A05A"),
        ['L'] = Brush("#C9A0F2"),
        ['g'] = Brush("#9A9DA3"),
        ['T'] = Brush("#FFFFFF"),
        ['W'] = Brush("#FFFFFF"),
    };

    /// <summary>The brush of a colour code; null keeps the text's own colour.</summary>
    public static IBrush? For(char? colour) => colour is { } code ? Brushes.GetValueOrDefault(code) : null;

    private static IBrush Brush(string colour) => new ImmutableSolidColorBrush(Color.Parse(colour));
}

/// <summary>Text in the game's colours, from <see cref="TextRun"/>s.</summary>
public class GameTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<TextRun>?> RunsProperty =
        AvaloniaProperty.Register<GameTextBlock, IReadOnlyList<TextRun>?>(nameof(Runs));

    public GameTextBlock() => TextWrapping = TextWrapping.Wrap;

    public IReadOnlyList<TextRun>? Runs
    {
        get => GetValue(RunsProperty);
        set => SetValue(RunsProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RunsProperty)
            Inlines = Build(Runs);
    }

    public static InlineCollection Build(IReadOnlyList<TextRun>? runs)
    {
        var inlines = new InlineCollection();
        foreach (var run in runs ?? [])
        {
            var inline = new Run(run.Text);
            if (GameColours.For(run.Colour) is { } brush)
                inline.Foreground = brush;
            inlines.Add(inline);
        }
        return inlines;
    }
}

/// <summary>An entity's tooltip as in the game: dark box, coloured lines, nested ones indented.</summary>
public class GameTooltipView : Border
{
    private const double IndentWidth = 14;

    public static readonly StyledProperty<IReadOnlyList<TooltipLine>?> LinesProperty =
        AvaloniaProperty.Register<GameTooltipView, IReadOnlyList<TooltipLine>?>(nameof(Lines));

    private readonly StackPanel _panel = new() { Spacing = 2 };

    public GameTooltipView()
    {
        Child = _panel;
        IsVisible = false;
        Padding = new Thickness(12, 10);
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        Background = new ImmutableSolidColorBrush(Color.Parse("#16191D"));
        BorderBrush = new ImmutableSolidColorBrush(Color.Parse("#4A4335"));
    }

    public IReadOnlyList<TooltipLine>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LinesProperty)
            return;
        _panel.Children.Clear();
        foreach (var line in Lines ?? [])
        {
            _panel.Children.Add(new GameTextBlock
            {
                Runs = line.Runs,
                Margin = new Thickness(line.Indent * IndentWidth, 0, 0, 0),
                FontSize = 12,
                Foreground = new ImmutableSolidColorBrush(Color.Parse("#DADCE0")),
            });
        }
        IsVisible = _panel.Children.Count > 0;
    }
}
