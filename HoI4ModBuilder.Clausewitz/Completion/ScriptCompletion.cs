using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Completion;

public enum CompletionPosition
{
    /// <summary>Inside a comment or string: nothing to complete.</summary>
    None,
    /// <summary>Start of a statement: a key is expected.</summary>
    Key,
    /// <summary>After an operator: a value is expected.</summary>
    Value,
}

/// <param name="ReplaceStart">Start of the word being typed; completion replaces [ReplaceStart, caret).</param>
/// <param name="Prefix">The part of the word already typed.</param>
/// <param name="Path">Keys of the enclosing blocks, outermost first.</param>
/// <param name="Key">For <see cref="CompletionPosition.Value"/>: the key whose value is being typed.</param>
/// <param name="OperatorFollows">An operator already follows the word, so completing a key must not add <c>=</c>.</param>
public sealed record CompletionContext(
    CompletionPosition Position,
    int ReplaceStart,
    string Prefix,
    IReadOnlyList<string?> Path,
    string? Key,
    bool OperatorFollows)
{
    public static readonly CompletionContext None = new(CompletionPosition.None, 0, "", [], null, false);

    /// <summary>The vocabulary context for keys at this position.</summary>
    public string VocabularyContext(string folder) =>
        Path.Count == 0 ? ScriptVocabulary.RootContext(folder) : ScriptVocabulary.BlockContext(Path[^1]);
}

public enum CompletionItemKind
{
    Key,
    Value,
    /// <summary>A word from the current file that the vocabulary doesn't know.</summary>
    Word,
}

/// <param name="Count">How often the item was seen; used for ranking.</param>
/// <param name="InsertsBlock">For keys whose value is usually a block: insert <c>key = { }</c>.</param>
public sealed record CompletionItem(string Label, CompletionItemKind Kind, string? Detail, int Count, bool InsertsBlock);

/// <summary>Works out what can be typed at a position and what to suggest there.</summary>
public static class ScriptCompletion
{
    private const int MaxItems = 400;

    public static CompletionContext GetContext(SyntaxTree tree, int offset)
    {
        SyntaxToken? previous = null;
        SyntaxToken? word = null;
        SyntaxToken? next = null;

        foreach (var token in tree.Root.DescendantTokens())
        {
            if (token.IsMissing)
                continue;

            if (InsideComment(token, offset))
                return CompletionContext.None;
            if (token.Kind == SyntaxKind.EndOfFileToken)
                break;

            var span = token.Span;
            if (span.End < offset || (span.End == offset && token.Kind != SyntaxKind.WordToken))
            {
                previous = token;
                continue;
            }
            if (span.Start < offset && token.Kind == SyntaxKind.StringToken)
                return CompletionContext.None;
            if (span.Start < offset && token.Kind == SyntaxKind.WordToken)
            {
                word = token;
                continue;
            }
            next = token;
            break;
        }

        var replaceStart = word?.Position ?? offset;
        var prefix = word is null ? "" : word.Text[..(offset - word.Position)];
        var path = EnclosingKeys(tree.Root, replaceStart);
        var operatorFollows = next is not null && SyntaxFacts.IsOperator(next.Kind);

        if (previous is not null && SyntaxFacts.IsOperator(previous.Kind))
        {
            var key = (previous.Parent as PropertySyntax)?.Key.ValueText;
            return new CompletionContext(CompletionPosition.Value, replaceStart, prefix, path, key, operatorFollows);
        }
        return new CompletionContext(CompletionPosition.Key, replaceStart, prefix, path, null, operatorFollows);
    }

    /// <param name="folder">The file's folder relative to the mod root, e.g. <c>common/national_focus</c>.</param>
    public static IReadOnlyList<CompletionItem> GetItems(
        CompletionContext context, ScriptVocabulary? vocabulary, string folder, SyntaxTree tree)
    {
        var items = new Dictionary<string, CompletionItem>(StringComparer.Ordinal);

        if (vocabulary is not null)
        {
            if (context.Position == CompletionPosition.Key)
                AddKeys(items, context, vocabulary, folder);
            else if (context.Position == CompletionPosition.Value && context.Key is not null)
                AddValues(items, context, vocabulary, folder);
        }

        if (context.Position != CompletionPosition.None)
            AddDocumentWords(items, context, tree);

        return items.Values
            .Where(i => Matches(i.Label, context.Prefix) && i.Label != context.Prefix)
            .OrderBy(i => i.Kind == CompletionItemKind.Word ? 1 : 0)
            .ThenByDescending(i => i.Label.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(i => i.Count)
            .ThenBy(i => i.Label, StringComparer.Ordinal)
            .Take(MaxItems)
            .ToList();
    }

    private static void AddKeys(
        Dictionary<string, CompletionItem> items, CompletionContext context, ScriptVocabulary vocabulary, string folder)
    {
        // Unknown context (e.g. a mod-specific block): fall back to every key the game uses.
        var keys = vocabulary.GetKeys(context.VocabularyContext(folder)) ?? vocabulary.GlobalKeys;
        foreach (var (key, usage) in keys)
        {
            if (ScriptVocabulary.IsNumeric(key))
                continue;
            var detail = usage.IsMostlyBlock ? $"блок · {usage.Count}×" : $"{usage.Count}×";
            items[key] = new CompletionItem(key, CompletionItemKind.Key, detail, usage.Count, usage.IsMostlyBlock);
        }
    }

    private static void AddValues(
        Dictionary<string, CompletionItem> items, CompletionContext context, ScriptVocabulary vocabulary, string folder)
    {
        var usage = vocabulary.GetKeys(context.VocabularyContext(folder))?.GetValueOrDefault(context.Key!)
            ?? vocabulary.GlobalKeys.GetValueOrDefault(context.Key!);
        if (usage is null)
            return;
        foreach (var (value, count) in usage.Values)
            items[value] = new CompletionItem(value, CompletionItemKind.Value, $"{count}×", count, false);
    }

    /// <summary>
    /// Identifiers already used in this file (custom ids, focus names…), in the same role:
    /// keys where a key is expected, values where a value is.
    /// </summary>
    private static void AddDocumentWords(Dictionary<string, CompletionItem> items, CompletionContext context, SyntaxTree tree)
    {
        foreach (var token in tree.Root.DescendantTokens())
        {
            if (token.Kind != SyntaxKind.WordToken || token.IsMissing || token.Position == context.ReplaceStart)
                continue;
            var isKey = token.Parent is ScalarSyntax { Parent: PropertySyntax property } scalar && property.Key == scalar;
            if (isKey != (context.Position == CompletionPosition.Key))
                continue;
            var text = token.Text;
            if (text.Length < 2 || ScriptVocabulary.IsNumeric(text) || items.ContainsKey(text))
                continue;
            items[text] = new CompletionItem(text, CompletionItemKind.Word, "у цьому файлі", 0, false);
        }
    }

    /// <summary>Case-insensitive substring match, like IDE completion filters.</summary>
    private static bool Matches(string label, string prefix) =>
        prefix.Length == 0 || label.Contains(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool InsideComment(SyntaxToken token, int offset)
    {
        foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
        {
            // A comment runs to the end of the line, so its end position is still inside it.
            if (trivia.Kind == SyntaxKind.CommentTrivia && trivia.Position < offset && offset <= trivia.Span.End)
                return true;
        }
        return false;
    }

    /// <summary>Keys of the blocks enclosing <paramref name="position"/>, outermost first (null for keyless list blocks).</summary>
    private static List<string?> EnclosingKeys(IStatementContainer container, int position)
    {
        var path = new List<string?>();
        while (true)
        {
            BlockSyntax? inner = null;
            string? innerKey = null;
            foreach (var statement in container.Statements)
            {
                if (statement.FullSpan.Start > position)
                    break;
                var (key, block) = statement switch
                {
                    PropertySyntax { Value: BlockSyntax b } p => (p.Key.ValueText, b),
                    PropertySyntax { Value: TaggedBlockSyntax t } p => (p.Key.ValueText, t.Block),
                    ElementSyntax { Value: BlockSyntax b } => ((string?)null, b),
                    _ => (null, null),
                };
                if (block is not null && IsInside(block, position))
                {
                    inner = block;
                    innerKey = key;
                    break;
                }
            }
            if (inner is null)
                return path;
            path.Add(innerKey);
            container = inner;
        }
    }

    private static bool IsInside(BlockSyntax block, int position) =>
        block.OpenBrace.Span.End <= position
        && (block.CloseBrace.IsMissing || position <= block.CloseBrace.Span.Start);
}
