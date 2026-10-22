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
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.ViewModels.Entities;

/// <summary>A focus on the tree canvas.</summary>
public sealed partial class FocusNodeViewModel : GraphNodeViewModel
{
    public FocusNodeViewModel(string id) => Id = id;

    public string Id { get; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    /// <summary>Sprite name, e.g. <c>GFX_goal_generic_army_doctrines</c>.</summary>
    public string? IconName { get; set; }

    [ObservableProperty]
    public partial IImage? Icon { get; set; }

    /// <summary>Shown when the sprite can't be found, like the game's question-mark goal.</summary>
    public bool HasIcon => Icon is not null;

    partial void OnIconChanged(IImage? value) => OnPropertyChanged(nameof(HasIcon));

    /// <summary>Absolute grid position.</summary>
    public int GridX { get; set; }

    public int GridY { get; set; }

    /// <summary>Days to complete (cost is in weeks), for the tooltip.</summary>
    [ObservableProperty]
    public partial string? CostText { get; set; }

    public bool IsShared { get; set; }

    public required string FilePath { get; set; }

    public FocusDefinition Definition { get; set; } = null!;

    /// <summary>The first focus of a link the user is drawing.</summary>
    [ObservableProperty]
    public partial bool IsLinkSource { get; set; }
}

public enum FocusLinkMode
{
    None,
    Prerequisite,
    MutuallyExclusive,
    /// <summary>Picking the focus the source takes its position from.</summary>
    RelativePosition,
}

/// <summary>
/// A national focus tree drawn the way the game shows it: icons on a grid, prerequisite lines
/// (dashed when any one of several is enough) and red mutually-exclusive links. Focuses can be
/// dragged, added, removed and linked; everything is written straight into the script.
/// </summary>
public sealed partial class FocusTreeTabViewModel : EntityTabViewModel
{
    // Grid geometry in world units; the game's own spacing is close to this.
    public const double CellWidth = 112;
    public const double CellHeight = 140;
    public const double NodeWidth = 104;
    public const double NodeHeight = 104;
    public const double IconSize = 64;
    private const double Margin = 60;

    private static readonly IReadOnlySet<string> IconKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "icon" };

    private readonly Dictionary<string, (int X, int Y)> _layout = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sourceFiles = new(Paths.Comparer);
    private int _minX;
    private int _minY;
    private string? _pendingSelection;

    public FocusTreeTabViewModel(EntityContext context, string treeId, string filePath, Action<DocumentTabViewModel> close)
        : base(context, ContentKind.FocusTree, treeId, filePath, close)
    {
    }

    protected override string KindTitle => "Дерево фокусів";

    public ObservableCollection<FocusNodeViewModel> Nodes { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GraphEdge> Edges { get; private set; } = [];

    [ObservableProperty]
    public partial double CanvasWidth { get; private set; }

    [ObservableProperty]
    public partial double CanvasHeight { get; private set; }

    [ObservableProperty]
    public partial FocusNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinking))]
    public partial FocusLinkMode LinkMode { get; private set; }

    public bool IsLinking => LinkMode != FocusLinkMode.None;

    private FocusNodeViewModel? _linkSource;

    /// <summary>The line from the link source to the pointer while a link is being drawn.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GraphEdge> PreviewEdges { get; private set; } = [];

    /// <summary>
    /// Relative-position lines of one focus: to its anchor and from the focuses anchored to it.
    /// Shown for the hovered focus, otherwise the selected one; never for the whole tree.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<GraphEdge> RelativeEdges { get; private set; } = [];

    /// <summary>The focus under the pointer, set by the view.</summary>
    [ObservableProperty]
    public partial FocusNodeViewModel? HoveredNode { get; set; }

    partial void OnHoveredNodeChanged(FocusNodeViewModel? value) => UpdateRelativeEdges();

    private void UpdateRelativeEdges()
    {
        if ((HoveredNode ?? SelectedNode) is not { } focus)
        {
            RelativeEdges = [];
            return;
        }
        var byId = Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var edges = new List<GraphEdge>();
        if (focus.Definition.RelativeTo is { } anchorId && byId.TryGetValue(anchorId, out var anchor)
            && anchor != focus && RelativeLine(anchor, focus) is { } toAnchor)
            edges.Add(new GraphEdge(toAnchor, GraphEdgeStyle.Relative));
        foreach (var child in Nodes)
        {
            if (child != focus && string.Equals(child.Definition.RelativeTo, focus.Id, StringComparison.OrdinalIgnoreCase)
                && RelativeLine(focus, child) is { } toChild)
                edges.Add(new GraphEdge(toChild, GraphEdgeStyle.Relative));
        }
        RelativeEdges = edges;
    }

    /// <summary>Selects a focus once the tree has been read, e.g. when opened from the content view.</summary>
    public void Select(string focusId)
    {
        if (Nodes.FirstOrDefault(n => n.Id.Equals(focusId, StringComparison.OrdinalIgnoreCase)) is { } node)
            SelectedNode = node;
        else
            _pendingSelection = focusId;
    }

    partial void OnSelectedNodeChanged(FocusNodeViewModel? oldValue, FocusNodeViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
        ShowPanel(newValue is null ? null : BuildFocusPanel(newValue));
        UpdateRelativeEdges();
    }

    public override (ContentKind Kind, string Id)? RenameTarget =>
        SelectedNode is { } node ? (ContentKind.Focus, node.Id) : base.RenameTarget;

    public override void OnRenamed(ContentKind kind, string oldId, string newId)
    {
        base.OnRenamed(kind, oldId, newId);
        if (kind == ContentKind.Focus && SelectedNode?.Id == oldId)
            _pendingSelection = newId;
    }

    [RelayCommand]
    private Task RenameFocus(FocusNodeViewModel? node) =>
        node is null ? Task.CompletedTask : Context.Rename(ContentKind.Focus, node.Id, true);

    protected override bool DependsOn(string path) => _sourceFiles.Contains(path) || base.DependsOn(path);

    protected override void Refresh()
    {
        var files = new Dictionary<string, (string Text, SyntaxTree Tree)>(Paths.Comparer);
        (string Text, SyntaxTree Tree)? Load(string path)
        {
            if (files.TryGetValue(path, out var known))
                return known;
            return Read(path) is { } read ? files[path] = read : null;
        }

        var treeBlock = Load(FilePath) is { } own ? EntityLocator(FilePath, ContentKind.FocusTree, EntityId)(own.Tree) : null;
        if (treeBlock is null)
        {
            Error = $"Дерево фокусів «{EntityId}» не знайдено у {Context.Workspace.Relative(FilePath)}.";
            Nodes.Clear();
            Edges = [];
            SelectedNode = null;
            return;
        }
        Error = null;

        // The tree's own focuses, then the shared branches it pulls in (they may live in other files).
        var focuses = FocusTrees.ReadTree(treeBlock).Select(f => (Focus: f, File: FilePath)).ToList();
        var shared = new List<(FocusDefinition Focus, string File)>();
        foreach (var (file, entries) in Context.Content.Files)
        {
            if (!entries.Any(e => e.Kind == ContentKind.Focus) || Load(file) is not { } read)
                continue;
            foreach (var property in read.Tree.Root.Properties("shared_focus"))
            {
                if (FocusTrees.Read(property) is { } definition)
                    shared.Add((definition, file));
            }
        }
        var included = FocusTrees.CollectShared(FocusTrees.SharedReferences(treeBlock), shared.Select(s => s.Focus).ToList());
        focuses.AddRange(shared.Where(s => included.Contains(s.Focus)));

        _sourceFiles.Clear();
        _sourceFiles.UnionWith(focuses.Select(f => f.File));

        _layout.Clear();
        foreach (var (id, position) in FocusTrees.Layout(focuses.Select(f => f.Focus)))
            _layout[id] = position;
        _minX = _layout.Count == 0 ? 0 : _layout.Values.Min(p => p.X);
        _minY = _layout.Count == 0 ? 0 : _layout.Values.Min(p => p.Y);
        var maxX = _layout.Count == 0 ? 0 : _layout.Values.Max(p => p.X);
        var maxY = _layout.Count == 0 ? 0 : _layout.Values.Max(p => p.Y);
        CanvasWidth = (maxX - _minX + 1) * CellWidth + 2 * Margin;
        CanvasHeight = (maxY - _minY + 1) * CellHeight + 2 * Margin;

        SyncNodes(focuses);
        Edges = BuildEdges();
        if (HoveredNode is { } hovered && !Nodes.Contains(hovered))
            HoveredNode = null;
        UpdateRelativeEdges();

        var selectedId = _pendingSelection ?? SelectedNode?.Id;
        _pendingSelection = null;
        var selected = selectedId is null ? null : Nodes.FirstOrDefault(n => n.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        if (selected == SelectedNode)
            ShowPanel(selected is null ? null : BuildFocusPanel(selected)); // same node, fresh values
        else
            SelectedNode = selected;
    }

    private void SyncNodes(List<(FocusDefinition Focus, string File)> focuses)
    {
        var existing = Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<FocusNodeViewModel>();
        foreach (var (focus, file) in focuses)
        {
            if (result.Any(n => n.Id.Equals(focus.Id, StringComparison.OrdinalIgnoreCase)))
                continue; // A duplicated id: the game uses one; so do we.

            if (!existing.Remove(focus.Id, out var node))
                node = new FocusNodeViewModel(focus.Id) { FilePath = file, Width = NodeWidth, Height = NodeHeight };

            var (gridX, gridY) = _layout[focus.Id];
            node.FilePath = file;
            node.Definition = focus;
            node.IsShared = focus.IsShared;
            node.GridX = gridX;
            node.GridY = gridY;
            node.X = ToWorldX(gridX);
            node.Y = ToWorldY(gridY);
            node.Title = Context.Localisation.Text(focus.Id) ?? focus.Id;
            node.ShowProblems(Context.Problems.For(ContentKind.Focus, focus.Id));
            node.CostText = double.TryParse(focus.Cost, System.Globalization.CultureInfo.InvariantCulture, out var weeks)
                ? $"{weeks * 7:0} днів"
                : null;
            if (node.IconName != focus.Icon || node.Icon is null)
            {
                node.IconName = focus.Icon;
                var target = node;
                LoadPicture(focus.Icon, image => target.Icon = image);
            }
            result.Add(node);
        }

        Nodes.Clear();
        foreach (var node in result)
            Nodes.Add(node);
    }

    private double ToWorldX(int gridX) => Margin + (gridX - _minX) * CellWidth + (CellWidth - NodeWidth) / 2;

    private double ToWorldY(int gridY) => Margin + (gridY - _minY) * CellHeight;

    private List<GraphEdge> BuildEdges()
    {
        var byId = Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var edges = new List<GraphEdge>();
        foreach (var node in Nodes)
        {
            foreach (var group in node.Definition.Prerequisites)
            {
                var style = group.Count > 1 ? GraphEdgeStyle.Dashed : GraphEdgeStyle.Solid;
                foreach (var parentId in group)
                {
                    if (byId.TryGetValue(parentId, out var parent))
                        edges.Add(new GraphEdge(PrerequisiteLine(parent, node), style));
                }
            }
        }

        var seen = new HashSet<(string, string)>();
        foreach (var node in Nodes)
        {
            foreach (var otherId in node.Definition.MutuallyExclusive)
            {
                if (!byId.TryGetValue(otherId, out var other) || other == node)
                    continue;
                var pair = string.CompareOrdinal(node.Id, other.Id) < 0 ? (node.Id, other.Id) : (other.Id, node.Id);
                if (seen.Add(pair))
                    edges.Add(new GraphEdge(ExclusiveLine(node, other), GraphEdgeStyle.Exclusive));
            }
        }
        return edges;
    }

    private static Point IconCenter(FocusNodeViewModel node) => new(node.X + NodeWidth / 2, node.Y + 4 + IconSize / 2);

    /// <summary>Icon edge to icon edge, so both ends (anchor ring, arrow) stay visible; null when they touch.</summary>
    private static Point[]? RelativeLine(FocusNodeViewModel anchor, FocusNodeViewModel node)
    {
        const double inset = IconSize / 2 + 4;
        var from = IconCenter(anchor);
        var to = IconCenter(node);
        var delta = to - from;
        var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (length < inset * 2 + 8)
            return null;
        var step = delta / length * inset;
        return [from + step, to - step];
    }

    /// <summary>Down from the parent, across just above the child, down into it — as in the game.</summary>
    private static Point[] PrerequisiteLine(FocusNodeViewModel parent, FocusNodeViewModel child)
    {
        var start = new Point(parent.X + NodeWidth / 2, parent.Y + NodeHeight - 6);
        var end = new Point(child.X + NodeWidth / 2, child.Y);
        if (end.Y <= start.Y)
            return [start, end];
        var turn = end.Y - (CellHeight - NodeHeight) / 2;
        return [start, new Point(start.X, turn), new Point(end.X, turn), end];
    }

    private static Point[] ExclusiveLine(FocusNodeViewModel a, FocusNodeViewModel b)
    {
        var (left, right) = a.X <= b.X ? (a, b) : (b, a);
        var y1 = left.Y + IconSize / 2;
        var y2 = right.Y + IconSize / 2;
        return [new Point(left.X + (NodeWidth + IconSize) / 2, y1), new Point(right.X + (NodeWidth - IconSize) / 2, y2)];
    }

    private PropertyPanelViewModel? BuildFocusPanel(FocusNodeViewModel node)
    {
        if (Read(node.FilePath) is not { } read)
            return null;
        var locate = EntityLocator(node.FilePath, ContentKind.Focus, node.Id);
        if (locate(read.Tree) is not { } block)
            return null;

        var kind = node.IsShared ? "Спільний фокус" : "Фокус";
        var cost = node.CostText is null ? "" : $" · {node.CostText}";
        var file = node.FilePath;
        return BuildPanel(file, read.Text, block, locate, $"Focus:{node.Id}", node.Title, $"{kind} · {node.Id}{cost}",
            node.Definition.Position, EntitySchemas.For(ContentKind.Focus), "focus",
            description: Context.Localisation.Text(node.Id + "_desc"),
            picture: node.IconName,
            pictureEditor: SpritePicker(["GFX_goal_", "GFX_focus_"], sprite =>
            {
                using var action = Action("Зміна іконки");
                SetProperty(file, locate, "icon", sprite);
            }),
            skip: IconKey,
            pickers: new Dictionary<string, FieldPicker>
            {
                // Click the field, then the anchor focus on the tree; the focus keeps its place.
                ["relative_position_id"] = new(() => StartRelativePick(node.Id), () => ClearRelativePosition(node),
                    "не задано"),
            },
            titleKey: node.Id,
            descriptionKey: node.Id + "_desc",
            entity: (ContentKind.Focus, node.Id));
    }

    // ── Editing ───────────────────────────────────────────────

    /// <summary>World position of the top-left corner → the grid cell it is closest to.</summary>
    public (int X, int Y) ToGrid(double worldX, double worldY) =>
        ((int)Math.Round((worldX - Margin - (CellWidth - NodeWidth) / 2) / CellWidth) + _minX,
         (int)Math.Round((worldY - Margin) / CellHeight) + _minY);

    /// <summary>Puts a focus into another grid cell, keeping its <c>relative_position_id</c>.</summary>
    public void MoveFocus(FocusNodeViewModel node, int gridX, int gridY)
    {
        using var action = Action("Переміщення фокуса");
        var origin = node.Definition.RelativeTo is { } relative && _layout.TryGetValue(relative, out var position)
            ? position
            : (0, 0);
        var x = (gridX - origin.Item1).ToString();
        var y = (gridY - origin.Item2).ToString();
        Edit(node.FilePath, (text, tree) =>
            EntityLocator(node.FilePath, ContentKind.Focus, node.Id)(tree) is { } block
                ? [ScriptEdits.SetProperty(text, block, "x", x), ScriptEdits.SetProperty(text, block, "y", y)]
                : []);
        // Snap back to the grid now; the refresh after the edit confirms the position.
        node.X = ToWorldX(gridX);
        node.Y = ToWorldY(gridY);
    }

    /// <summary>Adds a new focus to the tree at a grid cell and selects it.</summary>
    public void AddFocus(int gridX, int gridY)
    {
        using var action = Action("Новий фокус");
        var ids = new HashSet<string>(_layout.Keys, StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (ids.Contains($"{EntityId}_focus_{number}"))
            number++;
        var id = $"{EntityId}_focus_{number}";

        Edit(FilePath, (text, tree) =>
        {
            if (EntityLocator(FilePath, ContentKind.FocusTree, EntityId)(tree) is not { } block)
                return [];
            var newLine = ScriptEdits.NewLine(text);
            var indent = block.Statements.LastOrDefault() is { } last
                ? ScriptEdits.IndentOf(text, last.Span.Start)
                : ScriptEdits.IndentOf(text, block.CloseBrace.Position) + "\t";
            var inner = indent + "\t";
            var statement = new StringBuilder()
                .Append("focus = {").Append(newLine)
                .Append(inner).Append("id = ").Append(id).Append(newLine)
                .Append(inner).Append("icon = GFX_goal_unknown").Append(newLine)
                .Append(inner).Append("x = ").Append(gridX).Append(newLine)
                .Append(inner).Append("y = ").Append(gridY).Append(newLine)
                .Append(inner).Append("cost = 10").Append(newLine)
                .Append(inner).Append("completion_reward = {").Append(newLine)
                .Append(inner).Append('}').Append(newLine)
                .Append(indent).Append('}')
                .ToString();
            return [ScriptEdits.InsertStatement(text, block, statement)];
        });
        _pendingSelection = id;
        RefreshNow();
    }

    [RelayCommand]
    private void DeleteFocus(FocusNodeViewModel? node)
    {
        using var action = Action("Видалення фокуса");
        node ??= SelectedNode;
        if (node is null)
            return;
        Edit(node.FilePath, (text, tree) =>
            ContentParser.Locate(tree, Context.Workspace.Relative(node.FilePath), ContentKind.Focus, node.Id) is { } property
                ? [ScriptEdits.RemoveStatement(text, property)]
                : []);
        if (SelectedNode == node)
            SelectedNode = null;
        RefreshNow();
    }

    [RelayCommand]
    private void StartPrerequisiteLink(FocusNodeViewModel? node) => StartLink(node, FocusLinkMode.Prerequisite);

    [RelayCommand]
    private void StartExclusiveLink(FocusNodeViewModel? node) => StartLink(node, FocusLinkMode.MutuallyExclusive);

    /// <summary>Starts picking the anchor of the selected focus (the inspector's relative_position_id field).</summary>
    public void StartRelativePick(string focusId) =>
        StartLink(Nodes.FirstOrDefault(n => n.Id.Equals(focusId, StringComparison.OrdinalIgnoreCase)), FocusLinkMode.RelativePosition);

    /// <summary>Called as the pointer moves over the tree: stretches the preview line to it.</summary>
    public void UpdateLinkPointer(Point world)
    {
        if (_linkSource is not { } source)
            return;
        var style = LinkMode == FocusLinkMode.MutuallyExclusive ? GraphEdgeStyle.Exclusive : GraphEdgeStyle.Preview;
        PreviewEdges = [new GraphEdge([IconCenter(source), world], style)];
    }

    private void StartLink(FocusNodeViewModel? node, FocusLinkMode mode)
    {
        node ??= SelectedNode;
        if (node is null)
            return;
        CancelLink();
        _linkSource = node;
        node.IsLinkSource = true;
        LinkMode = mode;
    }

    [RelayCommand]
    public void CancelLink()
    {
        if (_linkSource is not null)
            _linkSource.IsLinkSource = false;
        _linkSource = null;
        LinkMode = FocusLinkMode.None;
        PreviewEdges = [];
    }

    /// <summary>The second click of a link: connects the source focus with <paramref name="target"/>.</summary>
    public void CompleteLink(FocusNodeViewModel target)
    {
        var source = _linkSource;
        var mode = LinkMode;
        CancelLink();
        if (source is null || source == target)
            return;

        using var action = Action(mode switch
        {
            FocusLinkMode.Prerequisite => "Нова передумова",
            FocusLinkMode.MutuallyExclusive => "Взаємовиключення",
            _ => "Відносна позиція",
        });

        if (mode == FocusLinkMode.Prerequisite)
        {
            AddStatement(target, $"prerequisite = {{ focus = {source.Id} }}");
        }
        else if (mode == FocusLinkMode.MutuallyExclusive)
        {
            // The game expects the link on both sides.
            AddStatement(source, $"mutually_exclusive = {{ focus = {target.Id} }}");
            AddStatement(target, $"mutually_exclusive = {{ focus = {source.Id} }}");
        }
        else if (mode == FocusLinkMode.RelativePosition)
        {
            SetRelativePosition(source, target);
        }
        RefreshNow();
    }

    /// <summary>
    /// Makes <paramref name="node"/> take its position from <paramref name="anchor"/>, rewriting
    /// x/y so that it stays exactly where it is.
    /// </summary>
    public void SetRelativePosition(FocusNodeViewModel node, FocusNodeViewModel anchor)
    {
        using var action = Action("Відносна позиція");
        if (DependsOnPosition(anchor, node))
        {
            Context.ReportError($"«{anchor.Id}» сам відраховує позицію від «{node.Id}» — так вийшло б коло.");
            return;
        }
        var x = (node.GridX - anchor.GridX).ToString();
        var y = (node.GridY - anchor.GridY).ToString();
        Edit(node.FilePath, (text, tree) =>
            EntityLocator(node.FilePath, ContentKind.Focus, node.Id)(tree) is { } block
                ?
                [
                    ScriptEdits.SetProperty(text, block, "relative_position_id", anchor.Id),
                    ScriptEdits.SetProperty(text, block, "x", x),
                    ScriptEdits.SetProperty(text, block, "y", y),
                ]
                : []);
    }

    /// <summary>Removes <c>relative_position_id</c>, turning x/y into absolute values so the focus stays put.</summary>
    public void ClearRelativePosition(FocusNodeViewModel node)
    {
        using var action = Action("Відносна позиція");
        var x = node.GridX.ToString();
        var y = node.GridY.ToString();
        Edit(node.FilePath, (text, tree) =>
        {
            if (EntityLocator(node.FilePath, ContentKind.Focus, node.Id)(tree) is not { } block)
                return [];
            var edits = ScriptRead.All(block, "relative_position_id").Select(p => ScriptEdits.RemoveStatement(text, p)).ToList();
            edits.Add(ScriptEdits.SetProperty(text, block, "x", x));
            edits.Add(ScriptEdits.SetProperty(text, block, "y", y));
            return edits;
        });
        RefreshNow();
    }

    /// <summary>Whether <paramref name="focus"/> (through its chain of anchors) is positioned from <paramref name="anchor"/>; itself counts.</summary>
    private bool DependsOnPosition(FocusNodeViewModel focus, FocusNodeViewModel anchor)
    {
        var byId = Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<FocusNodeViewModel>();
        for (var current = focus; current is not null && seen.Add(current);)
        {
            if (current == anchor)
                return true;
            current = current.Definition.RelativeTo is { } next ? byId.GetValueOrDefault(next) : null;
        }
        return false;
    }

    private void AddStatement(FocusNodeViewModel node, string statement) =>
        Edit(node.FilePath, (text, tree) =>
            EntityLocator(node.FilePath, ContentKind.Focus, node.Id)(tree) is { } block
                ? [ScriptEdits.InsertStatement(text, block, statement)]
                : []);

    [RelayCommand]
    private void OpenFocusInCode(FocusNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is not null)
            Context.OpenInCode(node.FilePath, node.Definition.Position);
    }
}
