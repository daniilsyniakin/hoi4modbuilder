using System.Globalization;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Schema;
using HoI4ModBuilder.Game.Validation;

namespace HoI4ModBuilder.ViewModels.Entities.Inspector;

/// <summary>
/// Turns an entity's script block into inspector rows. Script blocks (effects, triggers,
/// modifiers) follow the game's documentation: value types, descriptions, a command picker.
/// </summary>
/// <param name="identity">The object the rows belong to; prefixes remembered expansion states.</param>
/// <param name="expansion">Expanded/collapsed blocks, remembered across refreshes and selections.</param>
/// <param name="problems">Problems of the entity by path (<c>prerequisite#0/focus#1</c>).</param>
internal sealed class InspectorBuilder(
    string text,
    IInspectorEditor editor,
    IInspectorSuggestions suggestions,
    string identity,
    IDictionary<string, bool> expansion,
    IReadOnlyDictionary<string, FieldPicker>? pickers = null,
    IReadOnlyDictionary<string, Problem>? problems = null)
{
    // Small blocks open by themselves; long effect lists stay folded until asked for.
    private const int AutoExpandLimit = 6;

    /// <summary>The picker category for the mod's own scripted effects and triggers.</summary>
    public const string ModScriptsCategory = "Скриптові (мод)";

    private ScriptSchema Schema => suggestions.Schema;

    /// <summary>
    /// The entity's own block: known properties in schema order (missing ones as "not set"),
    /// then everything else in file order.
    /// </summary>
    /// <param name="contextKey">The key the entity is defined with (<c>focus</c>, <c>country_event</c>…), for suggestions.</param>
    /// <param name="rootContext">The kind of script the block itself is: Effect for scripted effects, Plain for entities.</param>
    public BlockNode BuildRoot(BlockSyntax block, string? contextKey, IReadOnlyList<SchemaProperty> schema, IReadOnlySet<string> skip,
        ScriptContext rootContext = ScriptContext.Plain)
    {
        var root = NewBlock(contextKey ?? "", InspectorPath.Root, -1, isMissing: false, expanded: true, contextKey, rootContext, block);
        var known = new HashSet<string>(schema.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var property in schema)
        {
            if (skip.Contains(property.Key))
                continue;
            var present = ScriptRead.All(block, property.Key).ToList();
            if (present.Count == 0)
            {
                root.Children.Add(Missing(property, InspectorPath.Root.Append(property.Key, 0), contextKey));
                continue;
            }
            for (var i = 0; i < present.Count; i++)
                root.Children.Add(Build(present[i], InspectorPath.Root.Append(property.Key, i), 0, contextKey, property, rootContext));
        }

        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in block.Properties())
        {
            var key = property.Key.ValueText;
            var occurrence = occurrences[key] = occurrences.GetValueOrDefault(key, -1) + 1;
            if (!known.Contains(key) && !skip.Contains(key))
                root.Children.Add(Build(property, InspectorPath.Root.Append(key, occurrence), 0, contextKey, null, rootContext));
        }

        // Missing schema keys are already rows; offer the rest.
        root.KeySuggestions = suggestions.Keys(contextKey)
            .Where(k => !known.Contains(k) && !skip.Contains(k))
            .ToList();
        return root;
    }

    /// <param name="parentContext">The kind of script the property sits in.</param>
    private InspectorNode Build(PropertySyntax property, InspectorPath path, int depth, string? contextKey,
        SchemaProperty? schema, ScriptContext parentContext)
    {
        var key = property.Key.ValueText;
        var command = parentContext is ScriptContext.Effect or ScriptContext.Trigger or ScriptContext.Modifier
            ? Schema.Find(parentContext, key)
            : null;

        InspectorNode node;
        switch (property.Value)
        {
            case BlockSyntax block:
            {
                // Entity properties start a kind of script (completion_reward: effects); inside script it follows the rules.
                var context = parentContext == ScriptContext.Plain && depth == 0
                    ? ScriptSchema.EntityBlock(key)
                    : Schema.ChildContext(parentContext, key);
                node = BuildBlock(key, block, path, depth, schema?.Type == SchemaValue.List, context);
                break;
            }
            case ScalarSyntax scalar:
            {
                var value = scalar.ValueText;
                // Ids are changed with F2 / Shift+F2, which can also update every reference.
                var kind = depth == 0 && key.Equals("id", StringComparison.OrdinalIgnoreCase)
                    ? ScalarKind.ReadOnly
                    : KindOf(schema?.Type ?? ShapeAsSchema(command?.Shape), value);
                node = new ScalarNode(key, path, depth, false, editor, kind, value,
                    kind == ScalarKind.Text ? suggestions.Values(contextKey, key) : [])
                {
                    Hint = kind == ScalarKind.ReadOnly ? null : suggestions.Describe(value),
                    Picker = depth == 0 ? pickers?.GetValueOrDefault(key) : null,
                };
                break;
            }
            default:
            {
                // rgb { … } and the like: rare enough to leave to the code editor.
                var span = property.Value.Span;
                node = new ScalarNode(key, path, depth, false, editor, ScalarKind.ReadOnly, text.Substring(span.Start, span.Length), []);
                break;
            }
        }

        node.Documentation = command is null ? null : Describe(command);
        node.Problem = problems?.GetValueOrDefault(path.ToString());
        return node;
    }

    private BlockNode BuildBlock(string key, BlockSyntax block, InspectorPath path, int depth, bool isList, ScriptContext context)
    {
        var node = NewBlock(key, path, depth, isMissing: false, expanded: block.Statements.Length <= AutoExpandLimit && depth == 0,
            key, context, block);
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var itemIndex = 0;
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case PropertySyntax property:
                {
                    var childKey = property.Key.ValueText;
                    var occurrence = occurrences[childKey] = occurrences.GetValueOrDefault(childKey, -1) + 1;
                    node.Children.Add(Build(property, path.Append(childKey, occurrence), depth + 1, key, null, context));
                    break;
                }
                case ElementSyntax element:
                {
                    var index = itemIndex++;
                    var itemText = element.Value is ScalarSyntax scalar ? scalar.ValueText : "{ … }";
                    node.Items.Add(new ListItemViewModel(itemText, suggestions.Describe(itemText), () => editor.RemoveItem(path, index)));
                    break;
                }
            }
        }

        node.IsList = node.Items.Count > 0 || (isList && node.Children.Count == 0);
        node.Summary = Summarize(node);
        return node;
    }

    private InspectorNode Missing(SchemaProperty property, InspectorPath path, string? contextKey)
    {
        InspectorNode node = property.Type switch
        {
            SchemaValue.Block or SchemaValue.List =>
                NewBlock(property.Key, path, 0, isMissing: true, expanded: false, property.Key, ScriptSchema.EntityBlock(property.Key), null,
                    isList: property.Type == SchemaValue.List),
            _ => new ScalarNode(property.Key, path, 0, true, editor, KindOf(property.Type, ""), "",
                property.Type == SchemaValue.Text ? suggestions.Values(contextKey, property.Key) : [])
            {
                Picker = pickers?.GetValueOrDefault(property.Key),
            },
        };
        node.Problem = problems?.GetValueOrDefault(path.ToString());
        return node;
    }

    private BlockNode NewBlock(string key, InspectorPath path, int depth, bool isMissing, bool expanded, string? blockKey,
        ScriptContext context, BlockSyntax? block, bool isList = false)
    {
        var expansionKey = $"{identity}|{path}";
        var node = new BlockNode(key, path, depth, isMissing, editor,
            !isMissing && expansion.TryGetValue(expansionKey, out var remembered) ? remembered : expanded,
            expansionKey, (k, value) => expansion[k] = value)
        {
            DefaultValue = childKey => suggestions.DefaultValue(blockKey, childKey),
            IsList = isList,
            Context = context,
        };
        if (!isMissing)
            node.KeySuggestions = suggestions.Keys(blockKey);

        if (!isMissing && node.IsScript)
        {
            // Counted now, so the new command's path (key#n) is known and it can open expanded.
            var existing = block?.Properties()
                .GroupBy(p => p.Key.ValueText, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            node.PickerFactory = () => new CommandPickerViewModel(
                suggestions.Commands(context),
                ScriptSchema.Categories(context).Append(ModScriptsCategory),
                choice =>
                {
                    expansion[$"{identity}|{path.Append(choice.Name, existing.GetValueOrDefault(choice.Name))}"] = true;
                    editor.AddProperty(path, choice.Name, choice.Template);
                });
        }
        return node;
    }

    private static string Describe(ScriptCommand command)
    {
        var scopes = command.Scopes.Count > 0 && !command.Scopes.Contains("any", StringComparer.OrdinalIgnoreCase)
            ? $"\n{string.Join(", ", command.Scopes)}"
            : "";
        return $"{command.Name}\n{command.Description}{scopes}".Trim();
    }

    private static SchemaValue? ShapeAsSchema(ValueShape? shape) => shape switch
    {
        ValueShape.Number => SchemaValue.Number,
        ValueShape.Bool => SchemaValue.Bool,
        _ => null,
    };

    private static ScalarKind KindOf(SchemaValue? type, string value)
    {
        if (type == SchemaValue.Bool || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return ScalarKind.Bool;
        if (type == SchemaValue.Number && (value.Length == 0 || IsNumber(value)) || type is null && IsNumber(value))
            return ScalarKind.Number;
        return ScalarKind.Text;
    }

    private static bool IsNumber(string value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>"focus: UKR_army, focus: UKR_navy" — what a folded block holds.</summary>
    private static string Summarize(BlockNode node)
    {
        var parts = node.Children.Take(3).Select(child => child switch
        {
            ScalarNode scalar => $"{scalar.Label}: {scalar.Text}",
            BlockNode block => $"{block.Label} {{…}}",
            _ => child.Label,
        }).Concat(node.Items.Take(4).Select(i => i.Text)).ToList();
        var more = node.Children.Count + node.Items.Count - parts.Count;
        if (parts.Count == 0)
            return "порожньо";
        return string.Join(", ", parts) + (more > 0 ? $" … ще {more}" : "");
    }
}
