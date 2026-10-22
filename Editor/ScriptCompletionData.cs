using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Media.Imaging;
using HoI4ModBuilder.Clausewitz.Completion;
using HoI4ModBuilder.Game.Sprites;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.Editor;

/// <summary>One entry of the completion list.</summary>
public sealed class ScriptCompletionData(
    CompletionItem item, CompletionContext context, CompletionController controller, GameAssets? assets)
    : ICompletionData
{
    private static readonly IBrush KeyBadge = new ImmutableSolidColorBrush(Color.Parse("#7E5B8C"));
    private static readonly IBrush ValueBadge = new ImmutableSolidColorBrush(Color.Parse("#4E7A55"));
    private static readonly IBrush WordBadge = new ImmutableSolidColorBrush(Color.Parse("#4E5157"));
    private static readonly IBrush DetailBrush = new ImmutableSolidColorBrush(Color.Parse("#868A91"));

    private Control? _content;

    public IImage? Image => null;

    public string Text => item.Label;

    public object Content => _content ??= BuildContent();

    // The detail is already shown inline; a side tooltip would only repeat it.
    public object? Description => null;

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var document = textArea.Document;

        // Keys get their operator (and braces, for block keys) so beginners don't have to know the syntax.
        if (item.Kind == CompletionItemKind.Key && !context.OperatorFollows)
        {
            var line = document.GetLineByOffset(completionSegment.Offset);
            var indent = document.GetText(TextUtilities.GetLeadingWhitespace(document, line));
            var newLine = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);

            if (item.InsertsBlock)
            {
                var head = $"{item.Label} = {{{newLine}{indent}\t";
                document.Replace(completionSegment, $"{head}{newLine}{indent}}}");
                textArea.Caret.Offset = completionSegment.Offset + head.Length;
            }
            else
            {
                var text = $"{item.Label} = ";
                document.Replace(completionSegment, text);
                textArea.Caret.Offset = completionSegment.Offset + text.Length;
                // Offer the values this key usually takes (yes/no, known ids…), if any are known.
                Dispatcher.UIThread.Post(() => controller.Show(isExplicit: true, onlyKnownValues: true));
            }
            return;
        }

        document.Replace(completionSegment, item.Label);
    }

    private Control BuildContent()
    {
        var (letter, badge) = item.Kind switch
        {
            CompletionItemKind.Key => ("K", KeyBadge),
            CompletionItemKind.Value => ("V", ValueBadge),
            _ => ("W", WordBadge),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinWidth = 260 };
        var badgeBorder = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Background = badge,
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = letter,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(badgeBorder);
        if (assets?.Sprites.Find(item.Label) is { } sprite)
            _ = ShowThumbnailAsync(badgeBorder, assets, sprite);
        var label = new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (item.Detail is { } detail)
        {
            var detailText = new TextBlock
            {
                Text = detail,
                Foreground = DetailBrush,
                FontSize = 11,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(detailText, 2);
            grid.Children.Add(detailText);
        }
        return grid;
    }

    /// <summary>Replaces the letter badge with the sprite itself once it's decoded.</summary>
    private static async Task ShowThumbnailAsync(Border badge, GameAssets assets, SpriteDefinition sprite)
    {
        if (await assets.LoadSpriteAsync(sprite) is not { } image)
            return;
        var thumbnail = new Image { Source = image.Image, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(thumbnail, BitmapInterpolationMode.HighQuality);
        badge.Width = badge.Height = 24;
        badge.Background = null;
        badge.Child = thumbnail;
    }
}
