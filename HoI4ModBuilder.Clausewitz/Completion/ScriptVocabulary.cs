using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Completion;

/// <summary>How often a key appears in one context, whether its value is a block, and which values it takes.</summary>
public sealed class KeyUsage
{
    /// <summary>Distinct values remembered per key; enough for yes/no-style keys without bloating huge lists.</summary>
    public const int MaxValues = 150;

    public int Count { get; set; }

    public int BlockCount { get; set; }

    public Dictionary<string, int> Values { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public bool IsMostlyBlock => BlockCount * 2 > Count;

    internal void AddValue(string value, int count = 1)
    {
        if (Values.TryGetValue(value, out var existing))
            Values[value] = existing + count;
        else if (Values.Count < MaxValues)
            Values[value] = count;
    }

    internal void Merge(KeyUsage other)
    {
        Count += other.Count;
        BlockCount += other.BlockCount;
        foreach (var (value, count) in other.Values)
            AddValue(value, count);
    }
}

/// <summary>
/// What keys and values appear where, learned from existing scripts (the game's and the mod's).
/// HoI4 has no machine-readable schema, but its own files are a very good one: inside
/// <c>focus = { }</c> you find <c>id</c>, <c>cost</c>, <c>prerequisite</c>…, and after
/// <c>has_war =</c> you find <c>yes</c>/<c>no</c>.
/// </summary>
public sealed partial class ScriptVocabulary
{
    public const int FormatVersion = 1;

    private Dictionary<string, KeyUsage>? _globalKeys;

    /// <summary>Context name → key → usage. See <see cref="RootContext"/> and <see cref="BlockContext"/>.</summary>
    public Dictionary<string, Dictionary<string, KeyUsage>> Contexts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Top level of a file in <paramref name="folder"/>, e.g. <c>common/national_focus</c>.</summary>
    public static string RootContext(string folder) =>
        "<root>:" + folder.Replace('\\', '/').Trim('/').ToLowerInvariant();

    /// <summary>Inside the block of a property called <paramref name="key"/>.</summary>
    public static string BlockContext(string? key) => key switch
    {
        null => "<list>",
        _ when DateRegex().IsMatch(key) => "<date>",
        _ when NumberRegex().IsMatch(key) => "<number>",
        _ => key,
    };

    /// <summary>True for keys and values that are pointless to suggest: numbers, dates.</summary>
    public static bool IsNumeric(string text) => NumberRegex().IsMatch(text) || DateRegex().IsMatch(text);

    public IReadOnlyDictionary<string, KeyUsage>? GetKeys(string context) =>
        Contexts.GetValueOrDefault(context);

    /// <summary>Every key regardless of context, for when the context itself is unknown.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, KeyUsage> GlobalKeys => _globalKeys ??= BuildGlobalKeys();

    /// <param name="folder">The file's folder relative to the game or mod root.</param>
    public void AddFile(SyntaxTree tree, string folder)
    {
        AddStatements(tree.Root, RootContext(folder));
        _globalKeys = null;
    }

    public void Merge(ScriptVocabulary other)
    {
        foreach (var (context, keys) in other.Contexts)
        {
            var target = GetOrAddContext(context);
            foreach (var (key, usage) in keys)
            {
                if (target.TryGetValue(key, out var existing))
                    existing.Merge(usage);
                else
                    target[key] = Clone(usage);
            }
        }
        _globalKeys = null;
    }

    private void AddStatements(IStatementContainer container, string context)
    {
        foreach (var statement in container.Statements)
        {
            switch (statement)
            {
                case PropertySyntax property:
                    var key = property.Key.ValueText;
                    var keys = GetOrAddContext(context);
                    if (!keys.TryGetValue(key, out var usage))
                        keys[key] = usage = new KeyUsage();
                    usage.Count++;

                    switch (property.Value)
                    {
                        case BlockSyntax block:
                            usage.BlockCount++;
                            AddStatements(block, BlockContext(key));
                            break;
                        case TaggedBlockSyntax tagged:
                            usage.AddValue(tagged.Tag.Text);
                            break;
                        case ScalarSyntax { IsQuoted: false, Token.IsMissing: false } scalar when !IsNumeric(scalar.ValueText):
                            usage.AddValue(scalar.ValueText);
                            break;
                    }
                    break;

                case ElementSyntax { Value: BlockSyntax block }:
                    AddStatements(block, BlockContext(null));
                    break;
            }
        }
    }

    private Dictionary<string, KeyUsage> GetOrAddContext(string context)
    {
        if (!Contexts.TryGetValue(context, out var keys))
            Contexts[context] = keys = new Dictionary<string, KeyUsage>(StringComparer.Ordinal);
        return keys;
    }

    private Dictionary<string, KeyUsage> BuildGlobalKeys()
    {
        var result = new Dictionary<string, KeyUsage>(StringComparer.Ordinal);
        foreach (var keys in Contexts.Values)
        {
            foreach (var (key, usage) in keys)
            {
                if (result.TryGetValue(key, out var existing))
                    existing.Merge(usage);
                else
                    result[key] = Clone(usage);
            }
        }
        return result;
    }

    private static KeyUsage Clone(KeyUsage usage)
    {
        var copy = new KeyUsage();
        copy.Merge(usage);
        return copy;
    }

    [GeneratedRegex(@"^-?\d+(\.\d+)?%?$")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(\.\d+)?$")]
    private static partial Regex DateRegex();
}
