using System.Globalization;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Schema;

namespace HoI4ModBuilder.Game.Validation;

public enum ProblemSeverity
{
    Warning,
    Error,
}

/// <param name="Kind">The entity the problem is in; null for problems outside any entity (a broken file).</param>
/// <param name="Path">
/// Where inside the entity: <c>prerequisite#0/focus#1</c> (key and occurrence per level); empty
/// for the entity as a whole.
/// </param>
public sealed record Problem(
    ProblemSeverity Severity,
    string Message,
    ContentKind? Kind,
    string? EntityId,
    string File,
    int Position,
    string Path);

/// <summary>What the validator may look up besides the scripts themselves.</summary>
/// <param name="SpriteExists">Null while sprites are still loading (sprite checks are skipped).</param>
/// <param name="HasLocalisation">Null when no language is chosen (text checks are skipped).</param>
/// <param name="SeenInGame">Whether the game's own scripts use <c>key</c> inside a block of <c>blockKey</c>.</param>
/// <param name="IsGameScript">Whether the game defines a scripted effect (or trigger) called <c>key</c>.</param>
public sealed record ValidationContext(
    ScriptSchema Schema,
    Func<string, bool>? SpriteExists,
    Func<string, bool>? HasLocalisation,
    Func<string?, string, bool> SeenInGame,
    Func<string, ScriptContext, bool> IsGameScript);

/// <summary>
/// Finds mistakes a modder would otherwise only see in the game (or its error.log): references to
/// focuses and events that don't exist, triggers used as effects, text where a number belongs,
/// missing icons and names.
/// </summary>
public static class ModValidator
{
    // Parameters inside modifier blocks: targeted_modifier = { tag = GER … }, tooltips.
    private static readonly HashSet<string> ModifierParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "tag", "custom_modifier_tooltip", "hidden_modifier",
    };

    private static readonly HashSet<string> FiringKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "country_event", "news_event", "state_event", "unit_leader_event", "operative_leader_event",
    };

    public static List<Problem> Validate(IReadOnlyList<(string File, string Relative, SyntaxTree Tree)> files, ValidationContext context)
    {
        var problems = new List<Problem>();
        var index = new ModIndex(files);

        foreach (var file in files)
        {
            var located = index.Entries.Where(e => e.File == file.File).ToList();

            // Syntax errors, attached to the entity they are in.
            foreach (var diagnostic in file.Tree.Diagnostics.Where(d => d.Severity == Clausewitz.DiagnosticSeverity.Error))
            {
                var owner = located.LastOrDefault(e => e.Property.Span.Contains(diagnostic.Span.Start));
                problems.Add(new Problem(ProblemSeverity.Error, diagnostic.Message, owner?.Entry.Kind, owner?.Entry.Id,
                    file.File, diagnostic.Span.Start, ""));
            }

            foreach (var item in located)
            {
                var checker = new EntityChecker(item, index, context, problems);
                checker.Check();
            }
        }

        // The same id twice: the game keeps one and silently drops the other.
        foreach (var group in index.Entries.Where(e => e.Entry.Kind != ContentKind.FocusTree).GroupBy(e => (e.Entry.Kind, e.Entry.Id)))
        {
            if (group.Count() < 2)
                continue;
            foreach (var duplicate in group)
            {
                problems.Add(new Problem(ProblemSeverity.Error, $"«{duplicate.Entry.Id}» визначено {group.Count()} рази",
                    duplicate.Entry.Kind, duplicate.Entry.Id, duplicate.File, duplicate.Property.Span.Start, ""));
            }
        }

        CheckFocusLayout(index, problems);
        return problems;
    }

    /// <summary>Two focuses of one tree in the same grid cell overlap in the game.</summary>
    private static void CheckFocusLayout(ModIndex index, List<Problem> problems)
    {
        foreach (var tree in index.Entries.Where(e => e.Entry.Kind == ContentKind.FocusTree))
        {
            if (tree.Property.Value is not BlockSyntax block)
                continue;
            var focuses = FocusTrees.ReadTree(block).ToList();
            var layout = FocusTrees.Layout(focuses);
            // Branches shown only under conditions (allow_branch, with everything that follows from
            // them) or moved by offset may share a cell on purpose.
            var conditional = ScriptRead.Blocks(block, "focus")
                .Where(f => ScriptRead.All(f, "allow_branch").Any() || ScriptRead.All(f, "offset").Any())
                .Select(f => ScriptRead.Scalar(f, "id"))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            bool grew;
            do
            {
                grew = false;
                foreach (var focus in focuses)
                {
                    if (!conditional.Contains(focus.Id) && focus.Prerequisites.Any(p => p.Any(conditional.Contains)))
                        grew = conditional.Add(focus.Id);
                }
            } while (grew);
            foreach (var cell in focuses.Where(f => !conditional.Contains(f.Id)).GroupBy(f => layout.GetValueOrDefault(f.Id)).Where(g => g.Count() > 1))
            {
                foreach (var focus in cell)
                {
                    var others = string.Join(", ", cell.Where(f => f != focus).Select(f => $"«{f.Id}»"));
                    problems.Add(new Problem(ProblemSeverity.Warning, $"Стоїть у тій самій клітинці, що й {others}",
                        ContentKind.Focus, focus.Id, tree.File, focus.Position, "x#0"));
                }
            }
        }
    }

    /// <summary>An entity with its file and syntax.</summary>
    internal sealed record Located(ContentEntry Entry, string File, PropertySyntax Property);

    /// <summary>Everything defined in the mod, for resolving references.</summary>
    internal sealed class ModIndex
    {
        public ModIndex(IReadOnlyList<(string File, string Relative, SyntaxTree Tree)> files)
        {
            foreach (var (file, relative, tree) in files)
            {
                var properties = ContentParser.PropertiesByPosition(tree);
                foreach (var entry in ContentParser.Flatten(ContentParser.Parse(tree, relative)))
                {
                    if (properties.TryGetValue(entry.Position, out var property))
                        Entries.Add(new Located(entry, file, property));
                }
                foreach (var ns in tree.Root.Properties("add_namespace"))
                {
                    if (ns.Value is ScalarSyntax scalar)
                        EventNamespaces.Add(scalar.ValueText);
                }
            }
            foreach (var group in Entries.GroupBy(e => e.Entry.Kind))
                Ids[group.Key] = group.Select(e => e.Entry.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var focus in Entries.Where(e => e.Entry.Kind == ContentKind.Focus))
            {
                if (FocusTrees.Read(focus.Property) is { } definition)
                    Focuses.TryAdd(definition.Id, definition);
            }
        }

        public List<Located> Entries { get; } = [];

        public Dictionary<ContentKind, HashSet<string>> Ids { get; } = [];

        public Dictionary<string, FocusDefinition> Focuses { get; } = new(StringComparer.Ordinal);

        public HashSet<string> EventNamespaces { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Has(ContentKind kind, string id) => Ids.TryGetValue(kind, out var ids) && ids.Contains(id);
    }

    /// <summary>The checks for one entity, collecting problems with their inspector paths.</summary>
    private sealed class EntityChecker(Located item, ModIndex index, ValidationContext context, List<Problem> problems)
    {
        private ContentKind Kind => item.Entry.Kind;

        private string Id => item.Entry.Id;

        public void Check()
        {
            if (item.Property.Value is not BlockSyntax block)
                return;

            switch (Kind)
            {
                case ContentKind.Focus:
                    CheckFocus(block);
                    break;
                case ContentKind.Event:
                    CheckEvent(block);
                    break;
                case ContentKind.Idea:
                    CheckPicture(PictureSprite(ScriptRead.Scalar(block, "picture"), "GFX_idea_", Id));
                    CheckName(Id);
                    break;
                case ContentKind.Decision:
                    if (ScriptRead.ScalarOrNested(block, "icon", "value") is { } icon)
                        CheckPicture(PictureSprite(icon, "GFX_decision_", Id));
                    CheckName(Id);
                    break;
            }

            if (Kind == ContentKind.FocusTree)
                return; // its focuses are checked as entities of their own

            // Script blocks: effects, triggers, modifiers.
            var scope = Kind == ContentKind.Event && item.Property.Key.ValueText.Equals("state_event", StringComparison.OrdinalIgnoreCase)
                ? "STATE"
                : Kind is ContentKind.ScriptedEffect or ContentKind.ScriptedTrigger ? null : "COUNTRY";
            switch (Kind)
            {
                case ContentKind.ScriptedEffect:
                    Walk(block, ScriptContext.Effect, null, null, "");
                    break;
                case ContentKind.ScriptedTrigger:
                    Walk(block, ScriptContext.Trigger, null, null, "");
                    break;
                default:
                    foreach (var (property, path) in Children(block, ""))
                    {
                        var blockContext = ScriptSchema.EntityBlock(property.Key.ValueText);
                        if (blockContext != ScriptContext.Plain && property.Value is BlockSyntax inner)
                            Walk(inner, blockContext, property.Key.ValueText, scope, path);
                    }
                    break;
            }
        }

        private void CheckFocus(BlockSyntax block)
        {
            var occurrence = 0;
            foreach (var prerequisite in ScriptRead.All(block, "prerequisite"))
            {
                if (prerequisite.Value is BlockSyntax inner)
                    CheckFocusReferences(inner, $"prerequisite#{occurrence}", "Передумова");
                occurrence++;
            }
            occurrence = 0;
            foreach (var exclusive in ScriptRead.All(block, "mutually_exclusive"))
            {
                if (exclusive.Value is BlockSyntax inner)
                {
                    CheckFocusReferences(inner, $"mutually_exclusive#{occurrence}", "Взаємовиключення з");
                    var focusOccurrence = 0;
                    foreach (var other in ScriptRead.Values(inner, "focus"))
                    {
                        if (index.Focuses.TryGetValue(other, out var definition) && !definition.MutuallyExclusive.Contains(Id))
                        {
                            Add(ProblemSeverity.Warning, $"«{other}» не вказує «{Id}» як взаємовиключний — гра чекає зв'язку з обох боків",
                                inner, $"mutually_exclusive#{occurrence}/focus#{focusOccurrence}");
                        }
                        focusOccurrence++;
                    }
                }
                occurrence++;
            }
            if (ScriptRead.Scalar(block, "relative_position_id") is { } anchor && !index.Focuses.ContainsKey(anchor))
                Add(ProblemSeverity.Error, $"Фокуса «{anchor}» немає", block, "relative_position_id#0");

            if (ScriptRead.ScalarOrNested(block, "icon", "value") is { } icon)
                CheckPicture(icon);
            CheckName(Id);
        }

        private void CheckFocusReferences(BlockSyntax block, string path, string what)
        {
            var occurrence = 0;
            foreach (var focus in ScriptRead.Values(block, "focus"))
            {
                if (!index.Focuses.ContainsKey(focus))
                    Add(ProblemSeverity.Error, $"{what} «{focus}»: такого фокуса немає", block, $"{path}/focus#{occurrence}");
                occurrence++;
            }
        }

        private void CheckEvent(BlockSyntax block)
        {
            if (ScriptRead.Scalar(block, "picture") is { } picture)
                CheckPicture(picture);
            if (ScriptRead.ScalarOrNested(block, "title", "text") is { } title)
                CheckText(title, "Немає тексту заголовка", "title#0");
            if (ScriptRead.ScalarOrNested(block, "desc", "text") is { } description)
                CheckText(description, "Немає тексту опису", "desc#0");

            var options = ScriptRead.All(block, "option").ToList();
            if (options.Count == 0 && !ScriptRead.Yes(block, "hidden"))
                Add(ProblemSeverity.Warning, "Подія без жодного варіанта", block, "");
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i].Value is BlockSyntax option && ScriptRead.Scalar(option, "name") is { } name)
                    CheckText(name, "Немає тексту варіанта", $"option#{i}/name#0");
            }
        }

        /// <summary>Walks a script block, checking every command against the documentation.</summary>
        private void Walk(BlockSyntax block, ScriptContext scriptContext, string? blockKey, string? scope, string path)
        {
            foreach (var (property, childPath) in Children(block, path))
            {
                var key = property.Key.ValueText;
                var schema = context.Schema;

                if (scriptContext == ScriptContext.Modifier)
                {
                    if (ModifierParameters.Contains(key))
                    {
                        if (property.Value is BlockSyntax hidden)
                            Walk(hidden, ScriptContext.Modifier, key, scope, childPath);
                        continue;
                    }
                    if (schema.IsLoaded && schema.Find(ScriptContext.Modifier, key) is null && !context.SeenInGame(blockKey, key))
                        Add(ProblemSeverity.Warning, $"Невідомий модифікатор «{key}»", property, childPath);
                    else if (property.Value is ScalarSyntax value && !IsNumberLike(value.ValueText))
                        Add(ProblemSeverity.Error, $"Модифікатор «{key}» має бути числом", property, childPath);
                    continue;
                }

                if (scriptContext == ScriptContext.Weight)
                {
                    if (property.Value is BlockSyntax weighted)
                        Walk(weighted, schema.ChildContext(scriptContext, key), key, scope, childPath);
                    continue;
                }

                if (scriptContext is not (ScriptContext.Effect or ScriptContext.Trigger))
                    continue;

                if (FiringKeys.Contains(key))
                    CheckFiredEvent(property, childPath);

                var command = schema.Find(scriptContext, key);
                var isTarget = property.Value is BlockSyntax && ScriptSchema.LooksLikeTarget(key);
                if (command is null && !schema.IsStructural(key) && !isTarget && schema.IsLoaded)
                {
                    var other = scriptContext == ScriptContext.Effect ? ScriptContext.Trigger : ScriptContext.Effect;
                    if (IsModScript(key, scriptContext) || context.IsGameScript(key, scriptContext))
                    {
                        // A scripted effect or trigger of the mod: fine.
                    }
                    else if (schema.Find(other, key) is not null && !context.SeenInGame(blockKey, key))
                    {
                        Add(ProblemSeverity.Error, scriptContext == ScriptContext.Effect
                            ? $"«{key}» — це умова, а тут потрібен ефект"
                            : $"«{key}» — це ефект, а тут потрібна умова", property, childPath);
                    }
                    else if (!context.SeenInGame(blockKey, key))
                    {
                        Add(ProblemSeverity.Warning, $"Невідома команда «{key}»", property, childPath);
                    }
                }

                if (command is not null)
                {
                    if (scope is not null && command.Scopes.Count > 0 && !command.Scopes.Contains("any", StringComparer.OrdinalIgnoreCase)
                        && !command.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                    {
                        Add(ProblemSeverity.Warning, $"«{key}» не діє тут: потрібна область {string.Join(" або ", command.Scopes)}, а тут {scope}",
                            property, childPath);
                    }
                    if (property.Value is ScalarSyntax value)
                        CheckValue(command, value.ValueText, property, childPath);
                }

                if (property.Value is BlockSyntax inner)
                {
                    // Flow control and random_list weights (50 = { … }) keep the scope; iterators and targets change it.
                    var keepsScope = ScriptSchema.IsFlowControl(key) || string.Equals(blockKey, "random_list", StringComparison.OrdinalIgnoreCase);
                    var changes = schema.IsScopeChange(key) || isTarget;
                    var childScope = keepsScope ? scope : ScriptSchema.ScopeOf(key) ?? (changes ? null : scope);
                    Walk(inner, schema.ChildContext(scriptContext, key), key, childScope, childPath);
                }
            }
        }

        private void CheckValue(ScriptCommand command, string value, PropertySyntax property, string path)
        {
            // Numbers may also be written as variable names, so only plainly wrong values count.
            if (command.Shape == ValueShape.Number && (value is "yes" or "no" || property.Value is ScalarSyntax { IsQuoted: true }))
                Add(ProblemSeverity.Error, $"«{command.Name}» очікує число, а не «{value}»", property, path);
            else if (command.Shape == ValueShape.Bool && value is not ("yes" or "no") && !IsVariable(value))
                Add(ProblemSeverity.Error, $"«{command.Name}» очікує yes або no", property, path);
        }

        /// <summary>An event id in the mod's own namespace must exist; others may be the game's.</summary>
        private void CheckFiredEvent(PropertySyntax property, string path)
        {
            var (id, idPath) = property.Value switch
            {
                ScalarSyntax scalar => (scalar.ValueText, path),
                BlockSyntax block => (ScriptRead.Scalar(block, "id"), path + "/id#0"),
                _ => (null, path),
            };
            if (id is null || index.Has(ContentKind.Event, id))
                return;
            var dot = id.LastIndexOf('.');
            if (dot > 0 && index.EventNamespaces.Contains(id[..dot]))
                Add(ProblemSeverity.Error, $"Події «{id}» немає", property, idPath);
        }

        private bool IsModScript(string key, ScriptContext scriptContext) =>
            index.Has(scriptContext == ScriptContext.Effect ? ContentKind.ScriptedEffect : ContentKind.ScriptedTrigger, key);

        private void CheckPicture(string? sprite)
        {
            if (sprite is not null && context.SpriteExists is { } exists && !exists(sprite))
                Add(ProblemSeverity.Warning, $"Спрайт «{sprite}» не знайдено ні в моді, ні в грі", item.Property, "");
        }

        private void CheckName(string key) => CheckText(key, "Немає назви в локалізації", "");

        private void CheckText(string key, string message, string path)
        {
            if (context.HasLocalisation is { } has && !has(key))
                Add(ProblemSeverity.Warning, $"{message} («{key}»)", item.Property, path);
        }

        private void Add(ProblemSeverity severity, string message, SyntaxNode at, string path) =>
            problems.Add(new Problem(severity, message, Kind, Id, item.File, at.Span.Start, path));

        /// <summary>Properties of a block with their paths (<c>key#occurrence</c> appended to <paramref name="path"/>).</summary>
        private static IEnumerable<(PropertySyntax Property, string Path)> Children(BlockSyntax block, string path)
        {
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in block.Properties())
            {
                var key = property.Key.ValueText;
                var occurrence = occurrences[key] = occurrences.GetValueOrDefault(key, -1) + 1;
                yield return (property, path.Length == 0 ? $"{key}#{occurrence}" : $"{path}/{key}#{occurrence}");
            }
        }

        private static string? PictureSprite(string? picture, string prefix, string id) =>
            picture is null ? prefix + id : picture.StartsWith("GFX_", StringComparison.OrdinalIgnoreCase) ? picture : prefix + picture;

        private static bool IsNumberLike(string value) =>
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || IsVariable(value);

        /// <summary>Constants (<c>@cost</c>), variables (<c>var:x</c>, <c>my_var</c> in math) and inline math pass.</summary>
        private static bool IsVariable(string value) =>
            value.StartsWith('@') || value.Contains(':') || value.Contains('[') || value.StartsWith('^');
    }
}
