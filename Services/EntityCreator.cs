using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Services;

/// <summary>Where a new entity should go, from what is selected: a file and a group inside it.</summary>
/// <param name="Group">An event namespace, an idea slot, a decision category.</param>
public sealed record CreationTarget(string? File, string? Group)
{
    public static CreationTarget None { get; } = new(null, null);
}

/// <summary>
/// Creates new entities: asks for the id (with a sensible suggestion), picks the file and the
/// place in it, and writes everything as one undoable action.
/// </summary>
public sealed partial class EntityCreator(ScriptWorkspace workspace, ContentIndex content, IPlatformService platform, string modName)
{
    public static string Title(ContentKind kind) => kind switch
    {
        ContentKind.FocusTree => "Дерево фокусів",
        ContentKind.Event => "Подія",
        ContentKind.Idea => "Ідея",
        ContentKind.Decision => "Рішення",
        ContentKind.Technology => "Технологія",
        ContentKind.Character => "Персонаж",
        ContentKind.Country => "Країна",
        ContentKind.ScriptedEffect => "Скриптовий ефект",
        ContentKind.ScriptedTrigger => "Скриптовий тригер",
        _ => kind.ToString(),
    };

    /// <returns>The created entity and its file, or null if cancelled; an error message on failure.</returns>
    public async Task<(ContentKind Kind, string Id, string File)?> CreateAsync(ContentKind kind, CreationTarget target, Action<string> reportError)
    {
        var taken = Ids(kind);
        var group = GroupFor(kind, target);
        var id = await platform.PromptAsync(NewTitle(kind), "Ідентифікатор", "Створити", null,
            value => Validate(kind, value, taken), Suggest(kind, group, taken));
        if (id is null)
            return null;
        if (kind == ContentKind.Event)
            group = EntityTemplates.Namespace(id);

        var file = FileFor(kind, target, group);
        using var action = workspace.Action($"{NewTitle(kind)} «{id}»");
        var error = Write(file, kind, id, group);
        // A category made up here (mod_decisions) must also be defined; existing ones already are.
        if (error is null && kind == ContentKind.Decision && group == DefaultCategory() && !Entries(kind).Any(e => e.Entry.Group == group))
            error = EnsureDecisionCategory(group);
        if (error is null && kind == ContentKind.Country)
            error = CreateCountryFiles(id);
        if (error is not null)
        {
            reportError(error);
            return null;
        }
        return (kind, id, file);
    }

    private static string NewTitle(ContentKind kind) => kind switch
    {
        ContentKind.FocusTree => "Нове дерево фокусів",
        ContentKind.Event => "Нова подія",
        ContentKind.Idea => "Нова ідея",
        ContentKind.Decision => "Нове рішення",
        ContentKind.Technology => "Нова технологія",
        ContentKind.Character => "Новий персонаж",
        ContentKind.Country => "Нова країна",
        ContentKind.ScriptedEffect => "Новий скриптовий ефект",
        ContentKind.ScriptedTrigger => "Новий скриптовий тригер",
        _ => kind.ToString(),
    };

    private static string? Validate(ContentKind kind, string value, IReadOnlySet<string> taken)
    {
        if (kind == ContentKind.Country && !TagPattern().IsMatch(value))
            return "Тег — три великі латинські літери або цифри.";
        if (kind == ContentKind.Event && EventPattern().IsMatch(value) is false)
            return "Id події має вигляд «простір_імен.номер».";
        if (!Renaming.IsValidId(value))
            return "Ім'я має бути одним словом, без пробілів, лапок і дужок.";
        return taken.Contains(value) ? "Таке ім'я вже є." : null;
    }

    private HashSet<string> Ids(ContentKind kind) =>
        content.Files.Values.SelectMany(ContentParser.Flatten).Where(e => e.Kind == kind).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

    private IEnumerable<(ContentEntry Entry, string File)> Entries(ContentKind kind) =>
        content.Files.SelectMany(f => ContentParser.Flatten(f.Value).Where(e => e.Kind == kind).Select(e => (e, f.Key)));

    /// <summary>The group the new entity joins: the selected one, the one of the selected file, or the usual one.</summary>
    private string? GroupFor(ContentKind kind, CreationTarget target)
    {
        if (target.Group is { } group)
            return group;
        var inFile = target.File is { } file
            ? Entries(kind).Where(e => Paths.AreEqual(e.File, file)).Select(e => e.Entry.Group).FirstOrDefault(g => g is not null)
            : null;
        return inFile ?? kind switch
        {
            ContentKind.Idea => "country",
            ContentKind.Decision => Entries(kind).Select(e => e.Entry.Group).FirstOrDefault(g => g is not null) ?? DefaultCategory(),
            ContentKind.Event => Entries(kind)
                .GroupBy(e => e.Entry.Group)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? Slug(),
            _ => null,
        };
    }

    /// <summary>A starting id: the next event number, or the mod's usual prefix with a plain name.</summary>
    private string Suggest(ContentKind kind, string? group, IReadOnlySet<string> taken)
    {
        if (kind == ContentKind.Event)
        {
            var ns = group ?? Slug();
            var last = taken
                .Where(id => id.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase))
                .Select(id => int.TryParse(id[(ns.Length + 1)..], out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
            return $"{ns}.{last + 1}";
        }
        if (kind == ContentKind.Country)
            return "";

        var name = kind switch
        {
            ContentKind.FocusTree => Slug() + "_focus",
            ContentKind.Idea => Prefix() + "new_idea",
            ContentKind.Decision => Prefix() + "new_decision",
            ContentKind.Technology => Prefix() + "new_technology",
            ContentKind.Character => Prefix() + "new_character",
            ContentKind.ScriptedEffect => Prefix() + "new_effect",
            _ => Prefix() + "new_trigger",
        };
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
            candidate = $"{name}_{n}";
        return candidate;
    }

    /// <summary>The <c>TAG_</c> most of the mod's ids start with (UKR_), or nothing.</summary>
    private string Prefix() =>
        content.Files.Values.SelectMany(ContentParser.Flatten)
            .Select(e => TagPrefix().Match(e.Id))
            .Where(m => m.Success)
            .GroupBy(m => m.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "";

    /// <summary>
    /// The file to write to: the selected one, a file already holding the group, any file of the
    /// kind, or a new one named after the mod.
    /// </summary>
    private string FileFor(ContentKind kind, CreationTarget target, string? group)
    {
        var folder = EntityTemplates.Folder(kind);
        bool InFolder(string file) =>
            string.Equals(Path.GetDirectoryName(workspace.Relative(file))?.Replace('\\', '/'), folder, StringComparison.OrdinalIgnoreCase);

        if (target.File is { } selected && InFolder(selected))
            return selected;
        var entries = Entries(kind).Where(e => InFolder(e.File)).ToList();
        return entries.FirstOrDefault(e => group is not null && e.Entry.Group == group).File
               ?? entries.Select(e => e.File).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
               ?? Path.Combine(workspace.RootPath, folder, Slug() + ".txt");
    }

    private string? Write(string file, ContentKind kind, string id, string? group) =>
        File.Exists(file)
            ? workspace.Edit(file, (text, _) => EntityTemplates.Add(kind, text, id, group))
            : workspace.CreateFile(file, EntityTemplates.NewFile(kind, id, group), withByteOrderMark: false);

    /// <summary>A decision category must be defined in common/decisions/categories; adds it there if the mod lacks it.</summary>
    private string? EnsureDecisionCategory(string category)
    {
        var folder = Path.Combine(workspace.RootPath, "common", "decisions", "categories");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.txt").Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
        foreach (var file in files)
        {
            if (workspace.ReadText(file) is { } text && SyntaxTree.Parse(text).Root.Properties(category).Any())
                return null;
        }
        var statement = EntityTemplates.NewDecisionCategory(category).TrimEnd('\n');
        return files.FirstOrDefault() is { } first
            ? workspace.Edit(first, (text, tree) => [ScriptEdits.InsertMultiLine(text, tree.Root, statement)])
            : workspace.CreateFile(Path.Combine(folder, Slug() + ".txt"), statement + "\n", withByteOrderMark: false);
    }

    private string DefaultCategory() => $"{Slug()}_decisions";

    /// <summary>A country also needs its look (common/countries) and its start (history/countries).</summary>
    private string? CreateCountryFiles(string tag)
    {
        var look = Path.Combine(workspace.RootPath, "common", "countries", tag + ".txt");
        var history = Path.Combine(workspace.RootPath, "history", "countries", $"{tag} - {tag}.txt");
        var error = File.Exists(look) ? null : workspace.CreateFile(look, EntityTemplates.NewCountryLook(), withByteOrderMark: false);
        return error ?? (File.Exists(history) ? null : workspace.CreateFile(history, EntityTemplates.NewCountryHistory(), withByteOrderMark: false));
    }

    /// <summary>The mod's name (or folder) as a lower-case word for file names and default ids.</summary>
    private string Slug() => ModNames.Slug(modName, workspace.RootPath);

    [GeneratedRegex("^[A-Z][A-Z0-9]{2}$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^[^.\s]+\.[^.\s]+$")]
    private static partial Regex EventPattern();

    [GeneratedRegex("^[A-Z]{3}_")]
    private static partial Regex TagPrefix();
}
