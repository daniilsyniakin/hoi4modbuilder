using System.Globalization;
using System.Text;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.FileSystem;

namespace HoI4ModBuilder.Game.Map;

/// <summary>A state as its history file sets it up at the start of the game (dated entries aside).</summary>
/// <param name="Position">Where <c>state = {</c> starts in the file.</param>
public sealed record StateInfo(
    int Id,
    string? NameKey,
    string? Owner,
    string? Controller,
    IReadOnlyList<string> Cores,
    IReadOnlyList<string> Claims,
    string? Category,
    long Manpower,
    IReadOnlyList<int> Provinces,
    IReadOnlyDictionary<string, int> Buildings,
    IReadOnlyDictionary<string, decimal> Resources,
    IReadOnlyList<(int Province, decimal Points)> VictoryPoints,
    string FullPath,
    bool IsMod,
    int Position);

/// <summary>A strategic region: weather and air/naval zones.</summary>
public sealed record StrategicRegion(int Id, string? NameKey, IReadOnlyList<int> Provinces);

/// <summary>States, strategic regions and country colours of the game with the mod over it.</summary>
public sealed class MapData
{
    private readonly Dictionary<int, StateInfo> _states;

    private MapData(List<StateInfo> states, List<StrategicRegion> regions, Dictionary<string, int> colours, HashSet<string> tags,
        Dictionary<string, string> rulingParties)
    {
        RulingParties = rulingParties;
        _states = states.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        States = _states.Values.OrderBy(s => s.Id).ToList();
        Regions = regions;
        CountryColours = colours;
        Tags = tags;
    }

    public static MapData Empty { get; } = new([], [], [], [], []);

    /// <summary>The ideology each country starts with (<c>set_politics</c> in its history), for its name: SOV_communism is "Soviet Union".</summary>
    public IReadOnlyDictionary<string, string> RulingParties { get; }

    public IReadOnlyList<StateInfo> States { get; }

    public IReadOnlyList<StrategicRegion> Regions { get; }

    /// <summary>Map colour of each country tag (0xRRGGBB).</summary>
    public IReadOnlyDictionary<string, int> CountryColours { get; }

    /// <summary>Every country tag the game knows (common/country_tags).</summary>
    public IReadOnlySet<string> Tags { get; }

    public StateInfo? State(int id) => _states.GetValueOrDefault(id);

    /// <param name="readText">Reads a file, e.g. from an open editor; null falls back to the disk.</param>
    public static MapData Load(GameFileSystem fileSystem, Func<string, string?>? readText = null)
    {
        string? Read(VirtualFile file)
        {
            if (readText?.Invoke(file.FullPath) is { } open)
                return open;
            try
            {
                return Encoding.UTF8.GetString(File.ReadAllBytes(file.FullPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        var states = new List<StateInfo>();
        foreach (var file in fileSystem.EnumerateFiles("history/states", "*.txt"))
        {
            if (Read(file) is { } text)
                states.AddRange(ReadStates(text, file.FullPath, file.Layer == FileLayer.Mod));
        }

        var regions = new List<StrategicRegion>();
        foreach (var file in fileSystem.EnumerateFiles("map/strategicregions", "*.txt"))
        {
            if (Read(file) is not { } text)
                continue;
            foreach (var block in ScriptRead.Blocks(SyntaxTree.Parse(text).Root, "strategic_region"))
            {
                if (int.TryParse(ScriptRead.Scalar(block, "id"), out var id))
                    regions.Add(new StrategicRegion(id, ScriptRead.Scalar(block, "name"), Numbers(block, "provinces")));
            }
        }

        var tags = new HashSet<string>(StringComparer.Ordinal);
        var countryFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in fileSystem.EnumerateFiles("common/country_tags", "*.txt"))
        {
            if (Read(file) is not { } text)
                continue;
            foreach (var property in SyntaxTree.Parse(text).Root.Properties())
            {
                var tag = property.Key.ValueText;
                if (tag.Length != 3 || property.Value is not ScalarSyntax path)
                    continue;
                tags.Add(tag);
                countryFiles[tag] = path.ValueText;
            }
        }

        // The country file's colour, overridden by common/countries/colors.txt as the game does.
        var colours = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (tag, path) in countryFiles)
        {
            if (fileSystem.Resolve("common/" + path) is { } file && Read(file) is { } text
                && ColourOf(SyntaxTree.Parse(text).Root.Properties().FirstOrDefault(p => p.Key.ValueText == "color")?.Value) is { } colour)
                colours[tag] = colour;
        }
        if (fileSystem.Resolve("common/countries/colors.txt") is { } colorsFile && Read(colorsFile) is { } colorsText)
        {
            foreach (var property in SyntaxTree.Parse(colorsText).Root.Properties())
            {
                if (property.Value is BlockSyntax block
                    && ColourOf(block.Properties().FirstOrDefault(p => p.Key.ValueText == "color")?.Value) is { } colour)
                    colours[property.Key.ValueText] = colour;
            }
        }
        var parties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in fileSystem.EnumerateFiles("history/countries", "*.txt"))
        {
            var tag = Path.GetFileName(file.RelativePath)[..Math.Min(3, Path.GetFileName(file.RelativePath).Length)];
            if (tag.Length != 3 || Read(file) is not { } text)
                continue;
            // Only the start: the top-level set_politics, not those under dates.
            if (ScriptRead.Blocks(SyntaxTree.Parse(text).Root, "set_politics").LastOrDefault() is { } politics
                && ScriptRead.Scalar(politics, "ruling_party") is { } party)
                parties[tag] = party;
        }
        return new MapData(states, regions, colours, tags, parties);
    }

    /// <summary>The states of one history file (usually one).</summary>
    public static IEnumerable<StateInfo> ReadStates(string text, string fullPath, bool isMod)
    {
        foreach (var property in SyntaxTree.Parse(text).Root.Properties().Where(p => p.Key.ValueText == "state"))
        {
            if (property.Value is not BlockSyntax block || !int.TryParse(ScriptRead.Scalar(block, "id"), out var id))
                continue;
            var history = ScriptRead.Blocks(block, "history").FirstOrDefault();
            var buildings = new Dictionary<string, int>(StringComparer.Ordinal);
            var points = new List<(int, decimal)>();
            if (history is not null)
            {
                // State-wide buildings only; province ones (naval bases, bunkers) are under province ids.
                foreach (var building in ScriptRead.Blocks(history, "buildings").SelectMany(b => b.Properties()))
                {
                    if (building.Value is ScalarSyntax level && int.TryParse(level.ValueText, out var value))
                        buildings[building.Key.ValueText] = buildings.GetValueOrDefault(building.Key.ValueText) + value;
                }
                foreach (var vp in ScriptRead.Blocks(history, "victory_points"))
                {
                    var numbers = vp.Statements.OfType<ElementSyntax>().Select(e => (e.Value as ScalarSyntax)?.ValueText).ToList();
                    if (numbers.Count >= 2 && int.TryParse(numbers[0], out var province)
                        && decimal.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        points.Add((province, value));
                }
            }
            var resources = ScriptRead.Blocks(block, "resources").SelectMany(r => r.Properties())
                .Where(r => r.Value is ScalarSyntax)
                .GroupBy(r => r.Key.ValueText)
                .ToDictionary(g => g.Key, g => decimal.TryParse(((ScalarSyntax)g.Last().Value).ValueText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var amount) ? amount : 0);

            yield return new StateInfo(
                id,
                ScriptRead.Scalar(block, "name"),
                history is null ? null : ScriptRead.Scalar(history, "owner"),
                history is null ? null : ScriptRead.Scalar(history, "controller"),
                history is null ? [] : ScriptRead.All(history, "add_core_of").Select(p => (p.Value as ScalarSyntax)?.ValueText).OfType<string>().ToList(),
                history is null ? [] : ScriptRead.All(history, "add_claim_by").Select(p => (p.Value as ScalarSyntax)?.ValueText).OfType<string>().ToList(),
                ScriptRead.Scalar(block, "state_category"),
                long.TryParse(ScriptRead.Scalar(block, "manpower"), out var manpower) ? manpower : 0,
                Numbers(block, "provinces"),
                buildings,
                resources,
                points,
                fullPath,
                isMod,
                property.Span.Start);
        }
    }

    private static List<int> Numbers(BlockSyntax block, string key) =>
        ScriptRead.Blocks(block, key)
            .SelectMany(b => b.Statements.OfType<ElementSyntax>())
            .Select(e => (e.Value as ScalarSyntax)?.ValueText)
            .Select(v => int.TryParse(v, out var n) ? n : (int?)null)
            .OfType<int>()
            .ToList();

    /// <summary><c>{ r g b }</c>, <c>rgb { r g b }</c> or <c>hsv { h s v }</c> (0–1) as 0xRRGGBB.</summary>
    public static int? ColourOf(ValueSyntax? value)
    {
        var (tag, block) = value switch
        {
            TaggedBlockSyntax tagged => (tagged.Tag.Text.ToLowerInvariant(), tagged.Block),
            BlockSyntax plain => ("rgb", plain),
            _ => ("", null),
        };
        if (block is null)
            return null;
        var numbers = block.Statements.OfType<ElementSyntax>()
            .Select(e => decimal.TryParse((e.Value as ScalarSyntax)?.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : (decimal?)null)
            .ToList();
        if (numbers.Count < 3 || numbers.Any(n => n is null))
            return null;
        var (a, b, c) = ((double)numbers[0]!, (double)numbers[1]!, (double)numbers[2]!);
        if (tag == "hsv")
            (a, b, c) = FromHsv(a, b, c);
        else if (a <= 1 && b <= 1 && c <= 1 && (a + b + c) > 0 && (a % 1 != 0 || b % 1 != 0 || c % 1 != 0))
            (a, b, c) = (a * 255, b * 255, c * 255);
        int Byte(double v) => (int)Math.Clamp(Math.Round(v), 0, 255);
        return (Byte(a) << 16) | (Byte(b) << 8) | Byte(c);
    }

    private static (double R, double G, double B) FromHsv(double h, double s, double v)
    {
        h = (h % 1 + 1) % 1 * 6;
        var i = (int)Math.Floor(h);
        var f = h - i;
        var (p, q, t) = (v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f)));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return (r * 255, g * 255, b * 255);
    }
}
