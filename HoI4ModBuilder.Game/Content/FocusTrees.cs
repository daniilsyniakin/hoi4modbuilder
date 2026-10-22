using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <param name="X">Grid column as written, relative to <paramref name="RelativeTo"/> when that is set.</param>
/// <param name="Y">Grid row as written.</param>
/// <param name="Prerequisites">
/// Each inner list is one <c>prerequisite = { … }</c> block: any one of its focuses is enough
/// (OR); every block must be satisfied (AND).
/// </param>
/// <param name="Position">Offset of the definition in its file.</param>
public sealed record FocusDefinition(
    string Id,
    int X,
    int Y,
    string? RelativeTo,
    string? Icon,
    string? Cost,
    IReadOnlyList<IReadOnlyList<string>> Prerequisites,
    IReadOnlyList<string> MutuallyExclusive,
    bool IsShared,
    int Position);

/// <summary>Reads national focus trees the way the game lays them out.</summary>
public static class FocusTrees
{
    /// <summary>Reads a <c>focus = { … }</c> or <c>shared_focus = { … }</c> definition.</summary>
    public static FocusDefinition? Read(PropertySyntax property)
    {
        if (property.Value is not BlockSyntax block || ScriptRead.Scalar(block, "id") is not { } id)
            return null;

        return new FocusDefinition(
            id,
            ScriptRead.Int(block, "x"),
            ScriptRead.Int(block, "y"),
            ScriptRead.Scalar(block, "relative_position_id"),
            ScriptRead.ScalarOrNested(block, "icon", "value"),
            ScriptRead.Scalar(block, "cost"),
            ScriptRead.Blocks(block, "prerequisite").Select(p => ScriptRead.Values(p, "focus")).Where(p => p.Count > 0).ToList(),
            ScriptRead.Blocks(block, "mutually_exclusive").SelectMany(m => ScriptRead.Values(m, "focus")).ToList(),
            property.Key.ValueText.Equals("shared_focus", StringComparison.OrdinalIgnoreCase),
            property.Span.Start);
    }

    /// <summary>The focuses defined inside a <c>focus_tree</c> block.</summary>
    public static IEnumerable<FocusDefinition> ReadTree(BlockSyntax tree) =>
        ScriptRead.All(tree, "focus").Select(Read).OfType<FocusDefinition>();

    /// <summary>The <c>shared_focus = X</c> references of a tree: the roots of shared branches it includes.</summary>
    public static IReadOnlyList<string> SharedReferences(BlockSyntax tree) => ScriptRead.Values(tree, "shared_focus");

    /// <summary>
    /// The shared focuses a tree pulls in: the referenced ones and every shared focus that
    /// (transitively) has one of them as a prerequisite.
    /// </summary>
    public static List<FocusDefinition> CollectShared(IEnumerable<string> roots, IReadOnlyCollection<FocusDefinition> shared)
    {
        var included = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        var result = new List<FocusDefinition>();
        bool added;
        do
        {
            added = false;
            foreach (var focus in shared)
            {
                if (result.Contains(focus))
                    continue;
                if (included.Contains(focus.Id) || focus.Prerequisites.Any(p => p.Any(included.Contains)))
                {
                    included.Add(focus.Id);
                    result.Add(focus);
                    added = true;
                }
            }
        } while (added);
        return result;
    }

    /// <summary>
    /// Absolute grid positions: <c>x</c>/<c>y</c> plus, for <c>relative_position_id</c>, the
    /// position of that focus. Unknown or circular references count as the origin.
    /// </summary>
    public static Dictionary<string, (int X, int Y)> Layout(IEnumerable<FocusDefinition> focuses)
    {
        var byId = new Dictionary<string, FocusDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var focus in focuses)
            byId.TryAdd(focus.Id, focus);

        var positions = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in byId.Keys)
            Resolve(id);
        return positions;

        (int X, int Y) Resolve(string id)
        {
            if (positions.TryGetValue(id, out var known))
                return known;
            if (!byId.TryGetValue(id, out var focus) || !resolving.Add(id))
                return (0, 0);

            (int X, int Y) origin = focus.RelativeTo is { } relative ? Resolve(relative) : (0, 0);
            resolving.Remove(id);
            return positions[id] = (origin.X + focus.X, origin.Y + focus.Y);
        }
    }
}
