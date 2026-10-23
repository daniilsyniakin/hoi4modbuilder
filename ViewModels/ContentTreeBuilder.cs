using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.ViewModels;

/// <summary>Arranges the mod's entities into the categories and virtual folders of the "Контент" view.</summary>
/// <param name="severity">The worst problem of an entity, from the mod check.</param>
/// <param name="title">An entity's localised name, if it has one.</param>
internal sealed class ContentTreeBuilder(string rootPath, Func<ContentKind, string, Game.Validation.ProblemSeverity?> severity,
    Func<ContentEntry, string?> title)
{
    private sealed record Located(ContentEntry Entry, string FilePath);

    // Idea slots and law groups the game ships with; the mod's own slots show their script key.
    private static readonly Dictionary<string, string> IdeaSlotTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["country"] = "Національні духи",
        ["political_advisor"] = "Політичні радники",
        ["theorist"] = "Теоретики",
        ["army_chief"] = "Начальники штабу армії",
        ["navy_chief"] = "Начальники штабу флоту",
        ["air_chief"] = "Начальники штабу авіації",
        ["high_command"] = "Верховне командування",
        ["tank_manufacturer"] = "Виробники танків",
        ["naval_manufacturer"] = "Виробники кораблів",
        ["aircraft_manufacturer"] = "Виробники літаків",
        ["materiel_manufacturer"] = "Виробники спорядження",
        ["industrial_concern"] = "Промислові концерни",
        ["economy"] = "Економічні закони",
        ["trade_laws"] = "Торгові закони",
        ["mobilization_laws"] = "Закони про призов",
    };

    public List<ContentNodeViewModel> Build(IReadOnlyDictionary<string, IReadOnlyList<ContentEntry>> files)
    {
        // Definition order: by file, then by position within it.
        var byKind = files
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(f => f.Value.Select(e => new Located(e, f.Key)))
            .ToLookup(l => l.Entry.Kind);

        return
        [
            Category(ContentKind.FocusTree, "Дерева фокусів", byKind[ContentKind.FocusTree].Count(),
                FocusTrees(byKind[ContentKind.FocusTree], byKind[ContentKind.Focus].ToList())),
            Category(ContentKind.Event, "Події", byKind[ContentKind.Event], namespaceName => namespaceName),
            Category(ContentKind.Idea, "Ідеї", byKind[ContentKind.Idea], slot => IdeaSlotTitles.GetValueOrDefault(slot, slot)),
            Category(ContentKind.Decision, "Рішення", byKind[ContentKind.Decision], category => category),
            // One file per branch (infantry, armor…).
            Category(ContentKind.Technology, "Технології", byKind[ContentKind.Technology], file => file),
            Category(ContentKind.Character, "Персонажі", byKind[ContentKind.Character]),
            Category(ContentKind.Country, "Країни", byKind[ContentKind.Country]),
            Category(ContentKind.State, "Області",
                byKind[ContentKind.State].OrderBy(s => int.TryParse(s.Entry.Id, out var id) ? id : int.MaxValue)),
            Category(ContentKind.ScriptedEffect, "Скриптові ефекти", byKind[ContentKind.ScriptedEffect]),
            Category(ContentKind.ScriptedTrigger, "Скриптові тригери", byKind[ContentKind.ScriptedTrigger]),
        ];
    }

    /// <summary>A category whose entities sit in virtual folders by <see cref="ContentEntry.Group"/>.</summary>
    private ContentNodeViewModel Category(ContentKind kind, string title, IEnumerable<Located> items, Func<string, string> groupTitle)
    {
        var list = items.ToList();
        var folders = list
            .GroupBy(i => i.Entry.Group ?? "")
            .Select(g => Folder(g.Key, groupTitle(g.Key), Entities(g), toolTip: g.Key, groupKind: kind));
        return Category(kind, title, list.Count, folders);
    }

    /// <summary>A category listing its entities directly.</summary>
    private ContentNodeViewModel Category(ContentKind kind, string title, IEnumerable<Located> items)
    {
        var entities = Entities(items);
        return Category(kind, title, entities.Count, entities);
    }

    private static ContentNodeViewModel Category(ContentKind kind, string title, int count, IEnumerable<ContentNodeViewModel> children)
    {
        var node = new ContentNodeViewModel(kind.ToString(), title, kind)
        {
            Description = count.ToString(),
            IsEmpty = count == 0,
        };
        foreach (var child in children)
            node.Children.Add(child);
        return node;
    }

    /// <summary>Each tree with its focuses, then the shared focuses that belong to no tree.</summary>
    private IEnumerable<ContentNodeViewModel> FocusTrees(IEnumerable<Located> trees, List<Located> sharedFocuses)
    {
        var treeList = trees.ToList();
        var nodes = Entities(treeList);
        for (var i = 0; i < nodes.Count; i++)
        {
            var tree = treeList[i];
            foreach (var focus in Entities(tree.Entry.Children.Select(f => new Located(f, tree.FilePath))))
            {
                focus.OwnerId = tree.Entry.Id;
                nodes[i].Children.Add(focus);
            }
            nodes[i].Description = nodes[i].Children.Count.ToString();
        }

        if (sharedFocuses.Count > 0)
            nodes.Add(Folder("shared", "Спільні фокуси", Entities(sharedFocuses)));
        return nodes;
    }

    private static ContentNodeViewModel Folder(string key, string name, List<ContentNodeViewModel> children, string? toolTip = null,
        ContentKind? groupKind = null)
    {
        var node = new ContentNodeViewModel("folder:" + key, name, null)
        {
            Description = children.Count.ToString(),
            ToolTip = toolTip,
            Group = groupKind is null ? null : key,
            GroupKind = groupKind,
        };
        foreach (var child in children)
            node.Children.Add(child);
        return node;
    }

    private List<ContentNodeViewModel> Entities(IEnumerable<Located> items)
    {
        // A mod may define the same id twice (usually by mistake); both stay visible, with distinct keys.
        var seen = new Dictionary<string, int>();
        return items.Select(item =>
        {
            var occurrence = seen[item.Entry.Id] = seen.GetValueOrDefault(item.Entry.Id) + 1;
            return new ContentNodeViewModel(occurrence == 1 ? item.Entry.Id : $"{item.Entry.Id}#{occurrence}", item.Entry.Id, item.Entry.Kind)
            {
                Title = title(item.Entry),
                Description = item.Entry.Detail,
                FilePath = item.FilePath,
                Position = item.Entry.Position,
                ToolTip = Path.GetRelativePath(rootPath, item.FilePath).Replace('\\', '/'),
                Severity = severity(item.Entry.Kind, item.Entry.Id),
                Group = item.Entry.Group,
                GroupKind = item.Entry.Kind,
            };
        }).ToList();
    }
}
