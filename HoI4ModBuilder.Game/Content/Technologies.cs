using System.Text;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.FileSystem;

namespace HoI4ModBuilder.Game.Content;

/// <summary>A technology of the game or the mod, as its tree shows it.</summary>
/// <param name="Position">Where its definition starts in the file.</param>
/// <param name="StartYear">The year it is historically researched; it's ahead of time before.</param>
/// <param name="ResearchCost">The cost in research days ×100 (1.5 = 150 days), constants resolved.</param>
/// <param name="Unlocks">Equipment and units it enables; a technology without a name of its own is called after the first.</param>
public sealed record TechnologyInfo(
    string Id,
    string RelativePath,
    string FullPath,
    bool IsMod,
    int Position,
    int? StartYear,
    decimal? ResearchCost,
    string? Folder,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> LeadsTo,
    IReadOnlyList<string> Unlocks);

/// <summary>Every technology the game would load: vanilla, DLCs and the mod (whose files replace vanilla's).</summary>
public sealed class TechnologyCatalog
{
    private readonly Dictionary<string, TechnologyInfo> _byId;
    private readonly ILookup<string, string> _prerequisites;

    private TechnologyCatalog(List<TechnologyInfo> all)
    {
        All = all;
        _byId = new Dictionary<string, TechnologyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var tech in all)
            _byId.TryAdd(tech.Id, tech);
        _prerequisites = all
            .SelectMany(t => t.LeadsTo.Select(next => (Next: next, From: t.Id)))
            .ToLookup(p => p.Next, p => p.From, StringComparer.OrdinalIgnoreCase);
    }

    public static TechnologyCatalog Empty { get; } = new([]);

    public IReadOnlyList<TechnologyInfo> All { get; }

    public TechnologyInfo? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Technologies that lead to <paramref name="id"/>.</summary>
    public IReadOnlyList<string> Prerequisites(string id) => _prerequisites[id].ToList();

    /// <param name="readText">Reads a file, e.g. from an open editor; falls back to the disk.</param>
    public static TechnologyCatalog Load(GameFileSystem fileSystem, Func<string, string?>? readText = null)
    {
        var all = new List<TechnologyInfo>();
        foreach (var file in fileSystem.EnumerateFiles("common/technologies", "*.txt"))
        {
            string? text = readText?.Invoke(file.FullPath);
            if (text is null)
            {
                try
                {
                    text = Encoding.UTF8.GetString(File.ReadAllBytes(file.FullPath));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }
            all.AddRange(Read(text, file.RelativePath, file.FullPath, file.Layer == FileLayer.Mod));
        }
        return new TechnologyCatalog(all);
    }

    /// <summary>The technologies of one file: <c>technologies = { @1936 = 2 tech_id = { … } }</c>.</summary>
    public static IEnumerable<TechnologyInfo> Read(string text, string relativePath, string fullPath, bool isMod)
    {
        var tree = SyntaxTree.Parse(text);
        foreach (var technologies in ScriptRead.Blocks(tree.Root, "technologies"))
        {
            var constants = technologies.Properties()
                .Where(p => p.Key.ValueText.StartsWith('@') && p.Value is ScalarSyntax)
                .GroupBy(p => p.Key.ValueText)
                .ToDictionary(g => g.Key, g => ((ScalarSyntax)g.Last().Value).ValueText);
            string? Resolve(string? value) => value is not null && value.StartsWith('@') ? constants.GetValueOrDefault(value) : value;

            foreach (var property in technologies.Properties())
            {
                if (property.Value is not BlockSyntax block || property.Key.ValueText.StartsWith('@'))
                    continue;
                var folder = ScriptRead.Blocks(block, "folder").FirstOrDefault() is { } folderBlock ? ScriptRead.Scalar(folderBlock, "name") : null;
                yield return new TechnologyInfo(
                    property.Key.ValueText,
                    relativePath,
                    fullPath,
                    isMod,
                    property.Span.Start,
                    int.TryParse(Resolve(ScriptRead.Scalar(block, "start_year")), out var year) ? year : null,
                    decimal.TryParse(Resolve(ScriptRead.Scalar(block, "research_cost")), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cost) ? cost : null,
                    folder,
                    ScriptRead.Blocks(block, "categories").SelectMany(Words).ToList(),
                    ScriptRead.Blocks(block, "path").Select(p => ScriptRead.Scalar(p, "leads_to_tech")).OfType<string>().ToList(),
                    ScriptRead.Blocks(block, "enable_equipments").Concat(ScriptRead.Blocks(block, "enable_subunits")).SelectMany(Words).ToList());
            }
        }
    }

    private static IEnumerable<string> Words(BlockSyntax block) =>
        block.Statements.OfType<ElementSyntax>().Select(e => e.Value).OfType<ScalarSyntax>().Select(s => s.ValueText);
}
