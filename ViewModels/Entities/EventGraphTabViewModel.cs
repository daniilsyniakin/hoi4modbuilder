using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Schema;
using HoI4ModBuilder.Game.Tooltips;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels.Entities;

/// <summary>An option row inside an event card; arrows to the events it fires start at its right edge.</summary>
public sealed partial class EventOptionViewModel(EventNodeViewModel owner, int index) : ViewModelBase
{
    public EventNodeViewModel Owner { get; } = owner;

    public int Index { get; } = index;

    public required string Text { get; init; }

    /// <summary>What the option does in words, a few lines at most.</summary>
    public required IReadOnlyList<TextRun> Effects { get; init; }

    public bool HasEffects => Effects.Count > 0;

    /// <summary>Offset from the top of the options area.</summary>
    public double Offset { get; init; }

    public double Height { get; init; }

    public int Position { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>An event card: picture, title and its options.</summary>
public sealed partial class EventNodeViewModel : GraphNodeViewModel
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>"новина · лише за викликом"…</summary>
    public string? Flags { get; init; }

    public bool HasFlags => !string.IsNullOrEmpty(Flags);

    /// <summary>The event the tab is about; drawn highlighted.</summary>
    public bool IsRoot { get; init; }

    /// <summary>Fired by the mod but not defined in it (a vanilla event or a typo).</summary>
    public bool IsMissing { get; init; }

    public string? FilePath { get; init; }

    public int Position { get; init; }

    public string Type { get; init; } = "country_event";

    public List<EventOptionViewModel> Options { get; } = [];

    [ObservableProperty]
    public partial IImage? Picture { get; set; }
}

/// <summary>Something that fires the root event: a focus, a decision, an option of an event outside the graph…</summary>
public sealed class EventSourceNodeViewModel : GraphNodeViewModel
{
    public required ContentKind SourceKind { get; init; }

    public required string SourceId { get; init; }

    public required string Title { get; init; }

    /// <summary>"Фокус", "Рішення"…, plus the option for events.</summary>
    public required string Caption { get; init; }

    public required string FilePath { get; init; }

    public int Position { get; init; }
}

/// <summary>
/// An event and its consequences as a graph: what fires it on the left, its options in the
/// middle, and the events each option leads to further right — following the chain.
/// </summary>
public sealed partial class EventGraphTabViewModel : EntityTabViewModel
{
    public const double EventWidth = 300;
    public const double HeaderHeight = 86;
    private const double OptionBaseHeight = 32;
    private const double EffectLineHeight = 16;
    private const int MaxEffectLines = 4;
    private const double OptionsPadding = 8;
    private const double SourceWidth = 240;
    private const double SourceHeight = 58;
    private const double ColumnGap = 120;
    private const double RowGap = 28;
    private const double Margin = 40;
    private const int MaxDepth = 8;
    private const int MaxEvents = 60;

    // Options are rows of the graph; the picture is changed by clicking it.
    private static readonly IReadOnlySet<string> EventPanelSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "option", "picture" };

    // Re-parsing every content file on each refresh is wasteful; keep trees while the text is unchanged.
    private readonly Dictionary<string, (string Text, SyntaxTree Tree)> _trees = new(Paths.Comparer);
    private readonly Dictionary<string, (EventDefinition Event, string File)> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BlockSyntax> _eventBlocks = new(StringComparer.OrdinalIgnoreCase);

    public EventGraphTabViewModel(EntityContext context, string eventId, string filePath, Action<DocumentTabViewModel> close)
        : base(context, ContentKind.Event, eventId, filePath, close)
    {
    }

    protected override string KindTitle => "Подія";

    public ObservableCollection<GraphNodeViewModel> Nodes { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GraphEdge> Edges { get; private set; } = [];

    [ObservableProperty]
    public partial double CanvasWidth { get; private set; }

    [ObservableProperty]
    public partial double CanvasHeight { get; private set; }

    /// <summary>What the panel shows: an event, an option or a source.</summary>
    [ObservableProperty]
    public partial object? Selection { get; set; }

    // The graph spans many files: any content file may fire or define an event.
    protected override bool DependsOn(string path) => true;

    partial void OnSelectionChanged(object? oldValue, object? newValue)
    {
        SetSelected(oldValue, false);
        SetSelected(newValue, true);
        ShowPanel(BuildSelectionPanel(newValue));
    }

    public override (ContentKind Kind, string Id)? RenameTarget => Selection switch
    {
        EventNodeViewModel { IsMissing: false } node => (ContentKind.Event, node.Id),
        EventOptionViewModel option => (ContentKind.Event, option.Owner.Id),
        EventSourceNodeViewModel source => (source.SourceKind, source.SourceId),
        _ => base.RenameTarget,
    };

    private static void SetSelected(object? item, bool value)
    {
        switch (item)
        {
            case GraphNodeViewModel node:
                node.IsSelected = value;
                break;
            case EventOptionViewModel option:
                option.IsSelected = value;
                break;
        }
    }

    protected override void Refresh()
    {
        var triggers = new List<(EventTrigger Trigger, string File)>();
        _events.Clear();
        _eventBlocks.Clear();
        foreach (var file in Context.Content.Files.Keys.ToList())
        {
            if (Parse(file) is not { } parsed)
                continue;
            var relative = Context.Workspace.Relative(file);
            var properties = ContentParser.PropertiesByPosition(parsed.Tree);
            foreach (var entry in ContentParser.Parse(parsed.Tree, relative).Where(e => e.Kind == ContentKind.Event))
            {
                if (properties.TryGetValue(entry.Position, out var property) && Events.Read(property) is { } definition
                    && _events.TryAdd(definition.Id, (definition, file)) && property.Value is BlockSyntax block)
                    _eventBlocks[definition.Id] = block;
            }
            triggers.AddRange(Events.FindTriggers(parsed.Tree, relative).Select(t => (t, file)));
        }
        foreach (var stale in _trees.Keys.Where(k => !Context.Content.Files.ContainsKey(k)).ToList())
            _trees.Remove(stale);

        if (!_events.TryGetValue(EntityId, out var root))
        {
            Error = $"Подію «{EntityId}» не знайдено.";
            Nodes.Clear();
            Edges = [];
            Selection = null;
            return;
        }
        Error = null;
        FilePath = root.File;

        // Breadth first along the options: each event lands in the column of its shortest path.
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [root.Event.Id] = 0 };
        var queue = new Queue<string>([root.Event.Id]);
        while (queue.Count > 0 && depths.Count < MaxEvents)
        {
            var id = queue.Dequeue();
            if (!_events.TryGetValue(id, out var current) || depths[id] >= MaxDepth)
                continue;
            foreach (var next in Fired(current.Event))
            {
                if (depths.TryAdd(next, depths[id] + 1))
                    queue.Enqueue(next);
            }
        }

        var sources = triggers
            .Where(t => t.Trigger.EventId.Equals(root.Event.Id, StringComparison.OrdinalIgnoreCase)
                        && !(t.Trigger.SourceKind == ContentKind.Event && depths.ContainsKey(t.Trigger.SourceId)))
            .DistinctBy(t => (t.Trigger.SourceKind, t.Trigger.SourceId, t.Trigger.Detail))
            .ToList();

        var previousSelection = SelectionKey(Selection);
        Nodes.Clear();
        var eventNodes = new Dictionary<string, EventNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var columnBottoms = new Dictionary<int, double>();
        var firstColumn = sources.Count > 0 ? 1 : 0;

        foreach (var (trigger, file) in sources)
        {
            var node = new EventSourceNodeViewModel
            {
                SourceKind = trigger.SourceKind,
                SourceId = trigger.SourceId,
                Title = SourceTitle(trigger),
                Caption = SourceCaption(trigger),
                FilePath = file,
                Position = trigger.Position,
                Width = SourceWidth,
                Height = SourceHeight,
            };
            Place(node, 0, columnBottoms);
            node.X += EventWidth - SourceWidth; // right-aligned in its column, close to the arrow
            Nodes.Add(node);
        }

        foreach (var (id, depth) in depths.OrderBy(d => d.Value))
        {
            var node = CreateEventNode(id, isRoot: depth == 0);
            Place(node, depth + firstColumn, columnBottoms);
            eventNodes[id] = node;
            Nodes.Add(node);
        }

        Edges = BuildEdges(eventNodes);
        CanvasWidth = Nodes.Count == 0 ? 0 : Nodes.Max(n => n.X + n.Width) + Margin;
        CanvasHeight = Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Y + n.Height) + Margin;

        Selection = FindBySelectionKey(previousSelection) ?? eventNodes[root.Event.Id];
    }

    private (string Text, SyntaxTree Tree)? Parse(string file)
    {
        if (Context.Workspace.ReadText(file) is not { } text)
            return null;
        if (_trees.TryGetValue(file, out var cached) && cached.Text == text)
            return cached;
        return _trees[file] = (text, SyntaxTree.Parse(text));
    }

    private static IEnumerable<string> Fired(EventDefinition definition) =>
        definition.ImmediateEvents.Concat(definition.Options.SelectMany(o => o.FiredEvents));

    private static void Place(GraphNodeViewModel node, int column, Dictionary<int, double> bottoms)
    {
        node.X = Margin + column * (EventWidth + ColumnGap);
        node.Y = bottoms.GetValueOrDefault(column, Margin);
        bottoms[column] = node.Y + node.Height + RowGap;
    }

    private EventNodeViewModel CreateEventNode(string id, bool isRoot)
    {
        if (!_events.TryGetValue(id, out var found))
        {
            return new EventNodeViewModel
            {
                Id = id,
                Title = Context.Localisation.Text(id + ".t") ?? id,
                Flags = "немає в моді — подія гри або помилка в id",
                IsMissing = true,
                Width = EventWidth,
                Height = HeaderHeight,
            };
        }

        var (definition, file) = found;
        var flags = new List<string>();
        if (definition.Type.Equals("news_event", StringComparison.OrdinalIgnoreCase))
            flags.Add("новина");
        if (definition.IsTriggeredOnly)
            flags.Add("лише за викликом");
        if (definition.IsHidden)
            flags.Add("прихована");

        var node = new EventNodeViewModel
        {
            Id = id,
            Title = Context.Localisation.Text(definition.Title) ?? definition.Title ?? id,
            Flags = string.Join(" · ", flags),
            IsRoot = isRoot,
            FilePath = file,
            Position = definition.Position,
            Type = definition.Type,
            Width = EventWidth,
        };

        var offset = 0.0;
        var optionBlocks = _eventBlocks.TryGetValue(id, out var eventBlock) ? ScriptRead.Blocks(eventBlock, "option").ToList() : [];
        var describer = new EffectDescriber(Context.Tooltips);
        foreach (var option in definition.Options)
        {
            // In the game's words when a language is chosen; as written otherwise.
            var described = Context.Localisation.Language is not null && optionBlocks.ElementAtOrDefault(option.Index) is { } optionBlock
                ? describer.Describe(optionBlock)
                : option.Effects.Select(e => new TooltipLine(0, [new TextRun(e)])).ToList();
            var lines = described.Take(MaxEffectLines).ToList();
            if (described.Count > MaxEffectLines)
                lines[^1] = new TooltipLine(0, [new TextRun($"… ще {described.Count - MaxEffectLines + 1}")]);
            var height = OptionBaseHeight + lines.Count * EffectLineHeight;
            node.Options.Add(new EventOptionViewModel(node, option.Index)
            {
                Text = Context.Localisation.Text(option.Name) ?? option.Name ?? $"Варіант {option.Index + 1}",
                Effects = lines.SelectMany((line, i) => line.Runs
                        .Select(r => r with { Text = r.Text.Replace('\n', ' ') })
                        .Prepend(new TextRun((i == 0 ? "" : "\n") + new string(' ', line.Indent * 2))))
                    .ToList(),
                Offset = offset,
                Height = height,
                Position = option.Position,
            });
            offset += height;
        }
        node.Height = HeaderHeight + offset + (node.Options.Count > 0 ? OptionsPadding : 0);
        node.ShowProblems(Context.Problems.For(ContentKind.Event, id));
        LoadPicture(definition.Picture, image => node.Picture = image);
        return node;
    }

    private List<GraphEdge> BuildEdges(Dictionary<string, EventNodeViewModel> eventNodes)
    {
        var edges = new List<GraphEdge>();
        foreach (var source in Nodes.OfType<EventSourceNodeViewModel>())
        {
            var root = eventNodes[EntityId];
            edges.Add(Flow(new Point(source.X + source.Width, source.Y + source.Height / 2), root));
        }

        foreach (var node in eventNodes.Values)
        {
            if (!_events.TryGetValue(node.Id, out var found))
                continue;
            foreach (var id in found.Event.ImmediateEvents)
            {
                if (eventNodes.TryGetValue(id, out var target))
                    edges.Add(Flow(new Point(node.X + node.Width, node.Y + HeaderHeight / 2), target));
            }
            foreach (var option in found.Event.Options)
            {
                var row = node.Options.First(o => o.Index == option.Index);
                var start = new Point(node.X + node.Width, node.Y + HeaderHeight + row.Offset + row.Height / 2);
                foreach (var id in option.FiredEvents)
                {
                    if (eventNodes.TryGetValue(id, out var target))
                        edges.Add(Flow(start, target));
                }
            }
        }
        return edges;

        static GraphEdge Flow(Point start, EventNodeViewModel target) =>
            new([start, new Point(target.X, target.Y + HeaderHeight / 2)], GraphEdgeStyle.Flow);
    }

    private string SourceTitle(EventTrigger trigger) => trigger.SourceKind switch
    {
        ContentKind.Event when _events.TryGetValue(trigger.SourceId, out var source) =>
            Context.Localisation.Text(source.Event.Title) ?? trigger.SourceId,
        ContentKind.ScriptedEffect => trigger.SourceId,
        _ => Context.Localisation.Text(trigger.SourceId) ?? trigger.SourceId,
    };

    private string SourceCaption(EventTrigger trigger) => trigger.SourceKind switch
    {
        ContentKind.Focus => $"Фокус · {trigger.SourceId}",
        ContentKind.Decision => $"Рішення · {trigger.SourceId}",
        ContentKind.ScriptedEffect => "Скриптовий ефект",
        ContentKind.Idea => $"Ідея · {trigger.SourceId}",
        ContentKind.Event when trigger.Detail == "immediate" => $"Подія {trigger.SourceId} · одразу",
        ContentKind.Event => $"Подія {trigger.SourceId} · «{Context.Localisation.Text(trigger.Detail) ?? trigger.Detail}»",
        _ => $"{trigger.SourceKind} · {trigger.SourceId}",
    };

    // ── Selection and the panel ───────────────────────────────

    private static string? SelectionKey(object? item) => item switch
    {
        EventNodeViewModel node => "event:" + node.Id,
        EventOptionViewModel option => $"option:{option.Owner.Id}:{option.Index}",
        EventSourceNodeViewModel source => $"source:{source.SourceKind}:{source.SourceId}",
        _ => null,
    };

    private object? FindBySelectionKey(string? key) => key is null
        ? null
        : Nodes.Cast<object>()
            .Concat(Nodes.OfType<EventNodeViewModel>().SelectMany(n => n.Options))
            .FirstOrDefault(item => SelectionKey(item) == key);

    private PropertyPanelViewModel? BuildSelectionPanel(object? item)
    {
        switch (item)
        {
            case EventNodeViewModel { FilePath: { } file } node when Read(file) is { } read:
            {
                var locate = EntityLocator(file, ContentKind.Event, node.Id);
                if (locate(read.Tree) is not { } block)
                    return null;
                var definition = _events[node.Id].Event;
                var prefixes = definition.Type.Equals("news_event", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "GFX_news_event_", "GFX_report_event_" }
                    : new[] { "GFX_report_event_", "GFX_news_event_" };
                return BuildPanel(file, read.Text, block, locate, $"Event:{node.Id}", node.Title, $"Подія · {node.Id}", node.Position,
                    EntitySchemas.For(ContentKind.Event), definition.Type,
                    description: Context.Localisation.Text(definition.Description), picture: definition.Picture,
                    pictureEditor: SpritePicker(prefixes, sprite =>
                    {
                        using var action = Action("Зміна зображення");
                        SetProperty(file, locate, "picture", sprite);
                    }),
                    skip: EventPanelSkip,
                    titleKey: definition.Title,
                    descriptionKey: definition.Description,
                    entity: (ContentKind.Event, node.Id));
            }
            case EventOptionViewModel option when option.Owner.FilePath is { } file && Read(file) is { } read:
            {
                Func<SyntaxTree, BlockSyntax?> locate = tree =>
                    EntityLocator(file, ContentKind.Event, option.Owner.Id)(tree) is { } eventBlock
                        ? ScriptRead.All(eventBlock, "option").ElementAtOrDefault(option.Index)?.Value as BlockSyntax
                        : null;
                if (locate(read.Tree) is not { } block)
                    return null;
                var nameKey = _events[option.Owner.Id].Event.Options.ElementAtOrDefault(option.Index)?.Name;
                return BuildPanel(file, read.Text, block, locate, $"Option:{option.Owner.Id}:{option.Index}", option.Text,
                    $"Варіант {option.Index + 1} · {option.Owner.Id}", option.Position, EntitySchemas.ForEventOption, "option",
                    titleKey: nameKey,
                    entity: (ContentKind.Event, option.Owner.Id),
                    pathPrefix: $"option#{option.Index}");
            }
            case EventSourceNodeViewModel source when Read(source.FilePath) is { } read:
            {
                var locate = EntityLocator(source.FilePath, source.SourceKind, source.SourceId);
                if (locate(read.Tree) is not { } block)
                    return null;
                return BuildPanel(source.FilePath, read.Text, block, locate, $"{source.SourceKind}:{source.SourceId}", source.Title,
                    source.Caption, source.Position, EntitySchemas.For(source.SourceKind),
                    source.SourceKind == ContentKind.Focus ? "focus" : null,
                    titleKey: HasOwnName(source.SourceKind) ? source.SourceId : null,
                    descriptionKey: HasOwnName(source.SourceKind) ? source.SourceId + "_desc" : null,
                    entity: (source.SourceKind, source.SourceId),
                    rootContext: RootContext(source.SourceKind));
            }
            case EventNodeViewModel { IsMissing: true } missing:
                return new PropertyPanelViewModel($"Missing:{missing.Id}", missing.Title, $"Подія · {missing.Id}", null)
                {
                    Description = "Ця подія не визначена в моді. Можливо, це подія з гри, або в id помилка.",
                };
            default:
                return null;
        }
    }

    /// <summary>Scripted effects and triggers are script themselves; other entities hold script in their properties.</summary>
    internal static ScriptContext RootContext(ContentKind kind) => kind switch
    {
        ContentKind.ScriptedEffect => ScriptContext.Effect,
        ContentKind.ScriptedTrigger => ScriptContext.Trigger,
        _ => ScriptContext.Plain,
    };

    /// <summary>Kinds whose name is localised under their id (events and scripted effects aren't).</summary>
    private static bool HasOwnName(ContentKind kind) => kind is not (ContentKind.Event or ContentKind.ScriptedEffect);

    /// <summary>Double click: open the clicked thing in its own editor.</summary>
    public void Open(object? item)
    {
        switch (item)
        {
            case EventNodeViewModel { IsRoot: false, IsMissing: false } node:
                Context.OpenEntity(ContentKind.Event, node.Id);
                break;
            case EventSourceNodeViewModel source:
                Context.OpenEntity(source.SourceKind, source.SourceId);
                break;
        }
    }

    [RelayCommand]
    private void OpenSelectionInCode()
    {
        switch (Selection)
        {
            case EventNodeViewModel { FilePath: { } file } node:
                Context.OpenInCode(file, node.Position);
                break;
            case EventOptionViewModel { Owner.FilePath: { } file } option:
                Context.OpenInCode(file, option.Position);
                break;
            case EventSourceNodeViewModel source:
                Context.OpenInCode(source.FilePath, source.Position);
                break;
        }
    }

    // ── Editing ───────────────────────────────────────────────

    /// <summary>Adds an empty option to an event.</summary>
    [RelayCommand]
    private void AddOption(EventNodeViewModel? node)
    {
        using var action = Action("Новий варіант");
        node ??= Selection as EventNodeViewModel ?? (Selection as EventOptionViewModel)?.Owner;
        if (node?.FilePath is not { } file)
            return;

        var letter = (char)('a' + node.Options.Count);
        Edit(file, (text, tree) =>
        {
            if (EntityLocator(file, ContentKind.Event, node.Id)(tree) is not { } block)
                return [];
            return [ScriptEdits.InsertStatement(text, block, MultiLine(text, block, "option = {", [$"name = {node.Id}.{letter}"]))];
        });
        RefreshNow();
        Selection = Nodes.OfType<EventNodeViewModel>().FirstOrDefault(n => n.Id == node.Id)?.Options.LastOrDefault();
    }

    /// <summary>
    /// The core of building a chain: creates a new event in the same file and makes the selected
    /// option fire it.
    /// </summary>
    [RelayCommand]
    private void AddFollowUpEvent(EventOptionViewModel? option)
    {
        using var action = Action("Нова подія-наслідок");
        option ??= Selection as EventOptionViewModel;
        if (option?.Owner.FilePath is not { } file)
            return;

        var id = NextEventId(option.Owner.Id);
        var type = option.Owner.Type;
        Edit(file, (text, tree) =>
        {
            if (EntityLocator(file, ContentKind.Event, option.Owner.Id)(tree) is not { } eventBlock
                || ScriptRead.All(eventBlock, "option").ElementAtOrDefault(option.Index)?.Value is not BlockSyntax optionBlock)
                return [];

            var newLine = ScriptEdits.NewLine(text);
            var newEvent = new StringBuilder()
                .Append(type).Append(" = {").Append(newLine)
                .Append("\tid = ").Append(id).Append(newLine)
                .Append("\ttitle = ").Append(id).Append(".t").Append(newLine)
                .Append("\tdesc = ").Append(id).Append(".d").Append(newLine)
                .Append("\tpicture = GFX_report_event_generic_read_write").Append(newLine)
                .Append("\tis_triggered_only = yes").Append(newLine)
                .Append(newLine)
                .Append("\toption = {").Append(newLine)
                .Append("\t\tname = ").Append(id).Append(".a").Append(newLine)
                .Append("\t}").Append(newLine)
                .Append('}')
                .ToString();
            return
            [
                ScriptEdits.InsertStatement(text, optionBlock, $"{type} = {id}"),
                ScriptEdits.InsertStatement(text, tree.Root, newLine + newEvent),
            ];
        });
        RefreshNow();
    }

    /// <summary>Next free number in the namespace of <paramref name="relatedId"/>: ukr.7 → ukr.12 if ukr.11 is the last.</summary>
    private string NextEventId(string relatedId)
    {
        var dot = relatedId.LastIndexOf('.');
        var ns = dot > 0 ? relatedId[..dot] : relatedId;
        var max = _events.Keys
            .Where(k => k.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase))
            .Select(k => int.TryParse(k[(ns.Length + 1)..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{ns}.{max + 1}";
    }

    /// <summary><c>head</c>, the lines one level deeper and a closing brace, indented to fit into <paramref name="block"/>.</summary>
    private static string MultiLine(string text, BlockSyntax block, string head, IEnumerable<string> lines)
    {
        var newLine = ScriptEdits.NewLine(text);
        var indent = block.Statements.LastOrDefault() is { } last
            ? ScriptEdits.IndentOf(text, last.Span.Start)
            : ScriptEdits.IndentOf(text, block.CloseBrace.Position) + "\t";
        var builder = new StringBuilder(head).Append(newLine);
        foreach (var line in lines)
            builder.Append(indent).Append('\t').Append(line).Append(newLine);
        return builder.Append(indent).Append('}').ToString();
    }
}
