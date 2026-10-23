using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Map;
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels.Entities;

namespace HoI4ModBuilder.ViewModels;

public enum MapMode
{
    Owners,
    Cores,
    Buildings,
    Resources,
    Regions,
    /// <summary>What the mod's scripts do with each state.</summary>
    Mod,
}

/// <summary>A colour on the map and what it means.</summary>
public sealed record MapLegendItem(IBrush Brush, string Text);

/// <summary>Something whose effect on the map can be shown alone: a focus tree, a focus, an event…</summary>
public sealed record MapFilter(ContentKind? Kind, string? Id, string Title)
{
    public override string ToString() => Title;
}

/// <summary>A mention of the selected state in the mod's scripts.</summary>
public sealed class MapReferenceViewModel(ModStateReference reference, string action, string owner, IRelayCommand open, IRelayCommand openCode)
{
    public ModStateReference Reference { get; } = reference;

    public string Action { get; } = action;

    public IBrush ActionBrush { get; } = MapTabViewModel.ActionBrush(reference.Reference.Action);

    public ContentKind? OwnerKind => Reference.Reference.Usage.Owner?.Kind;

    public string Owner { get; } = owner;

    public string Context => Reference.Reference.Usage.Context;

    public string LineText => $"рядок {Reference.Reference.Usage.Line}";

    public IRelayCommand Open { get; } = open;

    public IRelayCommand OpenCode { get; } = openCode;
}

/// <summary>The selected state: how it starts the game and what the mod does with it.</summary>
public sealed class MapStateViewModel(StateInfo state)
{
    public StateInfo State { get; } = state;

    public required string Name { get; init; }

    public string IdText => $"Область {State.Id}";

    public required string Owner { get; init; }

    public required string Cores { get; init; }

    public required string Claims { get; init; }

    public required string Category { get; init; }

    public string Manpower => State.Manpower.ToString("N0", CultureInfo.GetCultureInfo("uk-UA"));

    public required IReadOnlyList<string> Buildings { get; init; }

    public required IReadOnlyList<string> Resources { get; init; }

    public required IReadOnlyList<string> VictoryPoints { get; init; }

    public required string File { get; init; }

    public bool IsMod => State.IsMod;

    public IRelayCommand? OpenCode { get; init; }

    public required IReadOnlyList<MapReferenceViewModel> References { get; init; }

    public bool HasReferences => References.Count > 0;

    public bool HasBuildings => Buildings.Count > 0;

    public bool HasResources => Resources.Count > 0;

    public bool HasVictoryPoints => VictoryPoints.Count > 0;
}

/// <summary>
/// The map of the game with the mod over it: who owns what at the start, cores, buildings,
/// resources, strategic regions — and what the mod's scripts do with each state.
/// </summary>
public sealed partial class MapTabViewModel : DocumentTabViewModel
{
    private static readonly int[] ActionColours = [0xF2A05A, 0x62D26F, 0xF2C14E, 0x5EB0F2, 0xC9A0F2, 0x9A9DA3];
    private static readonly string[] ActionTitles = ["Передача", "Ядра", "Претензії", "Будівлі", "Перевірка власника", "Інше"];

    private readonly MapService _map;
    private readonly LocalisationService _localisation;
    private readonly ContentIndex _content;
    private readonly Action<ContentKind, string, string?> _openEntity;
    private readonly Action<string, int?> _openFile;

    // By province index: the state (index in States + 1, 0 none) and strategic region.
    private int[] _provinceState = [];
    private int[] _provinceRegion = [];
    private ProvinceMap? _indexed;
    private MapData? _indexedData;
    private int _renderGeneration;

    private readonly Func<StateInfo, EntityCardTabViewModel> _createEditor;
    private readonly Func<StateInfo, string?> _copyToMod;

    /// <param name="createEditor">An inspector for a state of the mod, as in the content view.</param>
    /// <param name="copyToMod">Copies a game state's file into the mod (an override); returns an error or null.</param>
    public MapTabViewModel(MapService map, LocalisationService localisation, ContentIndex content, string rootPath,
        Action<ContentKind, string, string?> openEntity, Action<string, int?> openFile, Action<DocumentTabViewModel> close,
        Func<StateInfo, EntityCardTabViewModel> createEditor, Func<StateInfo, string?> copyToMod)
        : base(Path.Combine(rootPath, "map"), rootPath, close)
    {
        _createEditor = createEditor;
        _copyToMod = copyToMod;
        _map = map;
        _localisation = localisation;
        _content = content;
        _openEntity = openEntity;
        _openFile = openFile;
        map.Changed += OnSourceChanged;
        localisation.Changed += OnSourceChanged;
        _ = map.EnsureDataAsync();
        _ = map.EnsureProvincesAsync();
        Update();
    }

    public override string Name => "Карта";

    public override string Breadcrumbs => "Карта гри з модом";

    public override ContentKind? EntityKind => ContentKind.State;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwners), nameof(IsCores), nameof(IsBuildings), nameof(IsResources), nameof(IsRegions), nameof(IsMod))]
    public partial MapMode Mode { get; set; } = MapMode.Owners;

    public bool IsOwners => Mode == MapMode.Owners;
    public bool IsCores => Mode == MapMode.Cores;
    public bool IsBuildings => Mode == MapMode.Buildings;
    public bool IsResources => Mode == MapMode.Resources;
    public bool IsRegions => Mode == MapMode.Regions;
    public bool IsMod => Mode == MapMode.Mod;

    [ObservableProperty]
    public partial WriteableBitmap? Picture { get; private set; }

    [ObservableProperty]
    public partial double MapWidth { get; private set; }

    [ObservableProperty]
    public partial double MapHeight { get; private set; }

    /// <summary>Pan and zoom, kept while switching tabs.</summary>
    public Matrix? ViewTransform { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; } = true;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapLegendItem> Legend { get; private set; } = [];

    /// <summary>The country whose cores and claims are shown.</summary>
    [ObservableProperty]
    public partial string? Country { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Countries { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<MapFilter> Filters { get; private set; } = [];

    [ObservableProperty]
    public partial MapFilter? Filter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopyToMod))]
    public partial MapStateViewModel? Selected { get; private set; }

    /// <summary>The selected state's inspector, when it is the mod's (game states are copied in first).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    public partial EntityCardTabViewModel? StateEditor { get; private set; }

    public bool HasEditor => StateEditor is not null;

    public bool CanCopyToMod => Selected is { IsMod: false };

    partial void OnSelectedChanged(MapStateViewModel? value)
    {
        // One inspector per state: kept while the same state stays selected (and its file reloads).
        var state = value?.State;
        if (StateEditor is { } editor && (state is null || !state.IsMod || editor.EntityId != state.Id.ToString() || !Paths.AreEqual(editor.FilePath, state.FullPath)))
        {
            editor.Discard();
            StateEditor = null;
        }
        if (StateEditor is null && state is { IsMod: true })
        {
            var created = _createEditor(state);
            created.IsActive = true;
            StateEditor = created;
        }
    }

    /// <summary>A game state into the mod: its file is copied over, then it can be edited here.</summary>
    [RelayCommand]
    private void CopyToMod()
    {
        if (Selected is { IsMod: false } selected)
            Error = _copyToMod(selected.State);
    }

    [ObservableProperty]
    public partial string? HoverText { get; private set; }

    partial void OnModeChanged(MapMode value)
    {
        if (value == MapMode.Cores && Country is null)
            Country = MostReferencedCountry() ?? Selected?.State.Owner ?? "GER";
        Render();
    }

    partial void OnCountryChanged(string? value) => Render();

    partial void OnFilterChanged(MapFilter? value) => Render();

    [RelayCommand]
    private void ShowMode(MapMode mode) => Mode = mode;

    /// <summary>The state under a world point (map pixels).</summary>
    public StateInfo? StateAt(Point world)
    {
        if (_map.Provinces is not { } map || map.At((int)world.X, (int)world.Y) is not { } province
            || map.IndexOf(province.Id) is not { } index || index >= _provinceState.Length || _provinceState[index] == 0)
            return null;
        return _map.Data.States[_provinceState[index] - 1];
    }

    public void HoverAt(Point world)
    {
        if (_map.Provinces is not { } map || map.At((int)world.X, (int)world.Y) is not { } province)
        {
            HoverText = null;
            return;
        }
        if (StateAt(world) is not { } state)
        {
            HoverText = province.Type == ProvinceType.Land ? $"Провінція {province.Id}" : $"Море · провінція {province.Id}";
            return;
        }
        var parts = new List<string> { $"{StateName(state)} ({state.Id})", CountryName(state.Owner) };
        if (Mode == MapMode.Mod && _map.References.TryGetValue(state.Id, out var references))
            parts.Add($"згадок у моді: {references.Count(Matches)}");
        parts.Add($"провінція {province.Id}");
        HoverText = string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    public void ClearHover() => HoverText = null;

    private Action<int>? _pick;
    private Action? _cancelPick;

    /// <summary>Picking a state for a field of an editor: the next click on a state gives its id.</summary>
    [ObservableProperty]
    public partial bool IsPicking { get; private set; }

    public void StartPick(Action<int> pick, Action cancel)
    {
        _cancelPick?.Invoke();
        _pick = pick;
        _cancelPick = cancel;
        IsPicking = true;
    }

    /// <summary>Esc: back to the editor without a value.</summary>
    public void CancelPick()
    {
        var cancel = _cancelPick;
        EndPick();
        cancel?.Invoke();
    }

    private void EndPick()
    {
        _pick = null;
        _cancelPick = null;
        IsPicking = false;
    }

    public void SelectAt(Point world)
    {
        if (_pick is { } pick)
        {
            if (StateAt(world) is { } picked)
            {
                EndPick();
                pick(picked.Id);
            }
            return;
        }
        var state = StateAt(world);
        Selected = state is null ? null : Details(state);
        Render();
    }

    /// <summary>Selects a state by id, e.g. from search.</summary>
    public void Select(int stateId)
    {
        if (_map.Data.State(stateId) is { } state)
        {
            Selected = Details(state);
            Render();
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        Error = _map.ProvincesError;
        IsLoading = Error is null && (_map.Provinces is null || !_map.IsDataLoaded);
        if (_map.Provinces is not { } map || !_map.IsDataLoaded)
            return;

        if (map != _indexed || _map.Data != _indexedData)
            Index(map, _map.Data);
        Countries = _map.Data.Tags.Order(StringComparer.Ordinal).ToList();
        Filters = BuildFilters();
        if (Filter is null || !Filters.Contains(Filter))
            Filter = Filters[0];
        if (Selected is { } selected)
            Selected = _map.Data.State(selected.State.Id) is { } fresh ? Details(fresh) : null;
        Render();
    }

    private void Index(ProvinceMap map, MapData data)
    {
        _indexed = map;
        _indexedData = data;
        _provinceState = new int[map.Provinces.Count];
        _provinceRegion = new int[map.Provinces.Count];
        for (var s = 0; s < data.States.Count; s++)
        {
            foreach (var id in data.States[s].Provinces)
            {
                if (map.IndexOf(id) is { } index)
                    _provinceState[index] = s + 1;
            }
        }
        for (var r = 0; r < data.Regions.Count; r++)
        {
            foreach (var id in data.Regions[r].Provinces)
            {
                if (map.IndexOf(id) is { } index)
                    _provinceRegion[index] = r + 1;
            }
        }
        MapWidth = map.Width;
        MapHeight = map.Height;
    }

    /// <summary>Repaints in the background; a newer request wins.</summary>
    private async void Render()
    {
        if (_map.Provinces is not { } map || !_map.IsDataLoaded || _provinceState.Length != map.Provinces.Count)
            return;
        var generation = ++_renderGeneration;
        var data = _map.Data;
        var stateColours = StateColours(data, out var legend);
        Legend = legend;

        var count = map.Provinces.Count;
        var colour = new int[count];
        var area = new int[count];
        var country = new int[count];
        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        var selected = 0;
        for (var p = 0; p < count; p++)
        {
            var province = map.Provinces[p];
            var s = _provinceState[p];
            if (Mode == MapMode.Regions)
            {
                var r = _provinceRegion[p];
                area[p] = r;
                colour[p] = province.Type == ProvinceType.Land || r == 0 ? (r == 0 ? MapPainter.NoState : MapPainter.Hashed(data.Regions[r - 1].Id))
                    : MapPainter.Mix(MapPainter.Hashed(data.Regions[r - 1].Id), MapPainter.Sea, 0.6);
                country[p] = r;
                continue;
            }
            colour[p] = province.Type switch
            {
                ProvinceType.Sea => MapPainter.Sea,
                ProvinceType.Lake => MapPainter.Lake,
                _ => s == 0 ? MapPainter.NoState : stateColours[s - 1],
            };
            area[p] = s;
            var owner = s == 0 ? null : data.States[s - 1].Owner;
            country[p] = owner is null ? 0 : owners.TryGetValue(owner, out var number) ? number : owners[owner] = owners.Count + 1;
        }
        if (Selected is { } chosen && Mode != MapMode.Regions)
            selected = data.States.ToList().FindIndex(st => st.Id == chosen.State.Id) + 1;

        var pixels = await Task.Run(() => MapPainter.Paint(map, colour, area, country, selected));
        if (generation != _renderGeneration)
            return;

        var bitmap = Picture is { } existing && existing.PixelSize.Width == map.Width && existing.PixelSize.Height == map.Height
            ? existing
            : new WriteableBitmap(new PixelSize(map.Width, map.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
        {
            if (buffer.RowBytes == map.Width * 4)
                Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
            else
                for (var y = 0; y < map.Height; y++)
                    Marshal.Copy(pixels, y * map.Width, buffer.Address + y * buffer.RowBytes, map.Width);
        }
        // A new instance, so the image notices the change.
        Picture = null;
        Picture = bitmap;
        IsLoading = false;
    }

    /// <summary>The colour of every state in the current mode, and what the colours mean.</summary>
    private int[] StateColours(MapData data, out IReadOnlyList<MapLegendItem> legend)
    {
        var colours = new int[data.States.Count];
        int OwnerColour(StateInfo state) =>
            state.Owner is { } owner && data.CountryColours.TryGetValue(owner, out var c) ? c : 0x5A5C60;
        int Faded(StateInfo state) => MapPainter.Mix(OwnerColour(state), 0x2A2C30, 0.8);
        var items = new List<MapLegendItem>();

        switch (Mode)
        {
            case MapMode.Owners:
                for (var i = 0; i < colours.Length; i++)
                    colours[i] = OwnerColour(data.States[i]);
                break;
            case MapMode.Cores:
            {
                var tag = Country ?? "";
                var own = data.CountryColours.TryGetValue(tag, out var c) ? c : 0x62D26F;
                for (var i = 0; i < colours.Length; i++)
                {
                    var state = data.States[i];
                    colours[i] = state.Cores.Contains(tag) ? (state.Owner == tag ? own : MapPainter.Mix(own, 0xFFFFFF, 0.3))
                        : state.Claims.Contains(tag) ? MapPainter.Mix(own, 0x2A2C30, 0.45)
                        : state.Owner == tag ? MapPainter.Mix(own, 0x000000, 0.45)
                        : Faded(state);
                }
                items.Add(Item(own, "Ядро, власна"));
                items.Add(Item(MapPainter.Mix(own, 0xFFFFFF, 0.3), "Ядро, чужа"));
                items.Add(Item(MapPainter.Mix(own, 0x2A2C30, 0.45), "Претензія"));
                items.Add(Item(MapPainter.Mix(own, 0x000000, 0.45), "Власна, не ядро"));
                break;
            }
            case MapMode.Buildings:
                for (var i = 0; i < colours.Length; i++)
                {
                    var b = data.States[i].Buildings;
                    var factories = b.GetValueOrDefault("arms_factory") + b.GetValueOrDefault("industrial_complex") + b.GetValueOrDefault("dockyard");
                    colours[i] = MapPainter.Mix(0x2D2F33, 0xF2C14E, Math.Min(factories / 15.0, 1));
                }
                items.Add(Item(0x2D2F33, "Немає заводів"));
                items.Add(Item(0xF2C14E, "15 і більше (військові, цивільні, верфі)"));
                break;
            case MapMode.Resources:
                for (var i = 0; i < colours.Length; i++)
                {
                    var total = (double)data.States[i].Resources.Values.Sum();
                    colours[i] = MapPainter.Mix(0x2D2F33, 0x5FD4E0, Math.Min(total / 40, 1));
                }
                items.Add(Item(0x2D2F33, "Без ресурсів"));
                items.Add(Item(0x5FD4E0, "40 і більше одиниць"));
                break;
            case MapMode.Mod:
                for (var i = 0; i < colours.Length; i++)
                {
                    var state = data.States[i];
                    colours[i] = _map.References.TryGetValue(state.Id, out var references) && references.Where(Matches).ToList() is { Count: > 0 } matching
                        ? ActionColours[(int)matching.Min(r => r.Reference.Action)]
                        : Faded(state);
                }
                for (var a = 0; a < ActionColours.Length; a++)
                    items.Add(Item(ActionColours[a], ActionTitles[a]));
                break;
        }
        legend = items;
        return colours;
    }

    private static MapLegendItem Item(int rgb, string text) => new(Brush(rgb), text);

    private static IBrush Brush(int rgb) => new ImmutableSolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    public static IBrush ActionBrush(StateAction action) => Brush(ActionColours[(int)action]);

    /// <summary>Whether a reference belongs to the chosen tree, focus or event (or there's no choice).</summary>
    private bool Matches(ModStateReference reference)
    {
        if (Filter is not { Kind: { } kind, Id: { } id })
            return true;
        var owner = reference.Reference.Usage.Owner;
        if (owner is null)
            return false;
        if (owner.Kind == kind && owner.Id == id)
            return true;
        // A focus tree stands for all its focuses.
        return kind == ContentKind.FocusTree && owner.Kind == ContentKind.Focus && TreeOf(owner.Id) == id;
    }

    private Dictionary<string, string>? _treeOfFocus;

    private string? TreeOf(string focus)
    {
        _treeOfFocus ??= _content.Files.Values.SelectMany(ContentParser.Flatten)
            .Where(e => e.Kind == ContentKind.FocusTree)
            .SelectMany(tree => tree.Children.Where(c => c.Kind == ContentKind.Focus).Select(c => (Focus: c.Id, Tree: tree.Id)))
            .GroupBy(p => p.Focus)
            .ToDictionary(g => g.Key, g => g.First().Tree);
        return _treeOfFocus.GetValueOrDefault(focus);
    }

    /// <summary>Everything that does something with states: focus trees first, then the rest by name.</summary>
    private IReadOnlyList<MapFilter> BuildFilters()
    {
        _treeOfFocus = null;
        var owners = _map.References.Values.SelectMany(r => r)
            .Select(r => r.Reference.Usage.Owner)
            .OfType<ContentEntry>()
            .DistinctBy(o => (o.Kind, o.Id))
            .ToList();
        var trees = owners.Where(o => o.Kind == ContentKind.Focus).Select(o => TreeOf(o.Id)).OfType<string>().Distinct()
            .Select(t => new MapFilter(ContentKind.FocusTree, t, $"Дерево фокусів: {t}"));
        var entities = owners
            .Select(o => new MapFilter(o.Kind, o.Id, $"{KindTitle(o.Kind)}: {_localisation.Text(o.Id) ?? o.Id}"))
            .OrderBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase);
        return [new MapFilter(null, null, "Увесь мод"), .. trees, .. entities];
    }

    private static string KindTitle(ContentKind kind) => kind switch
    {
        ContentKind.Focus => "Фокус",
        ContentKind.State => "Область",
        _ => EntityCreator.Title(kind),
    };

    private string? MostReferencedCountry() =>
        _map.References.Values.SelectMany(r => r).Select(r => r.Reference.Country).OfType<string>()
            .GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    private string StateName(StateInfo state) => _localisation.Text(state.NameKey ?? $"STATE_{state.Id}") ?? state.NameKey ?? $"STATE_{state.Id}";

    /// <summary>As the game calls it at the start: by its ruling party (SOV_communism is "Soviet Union", SOV alone "Russia").</summary>
    private string CountryName(string? tag)
    {
        if (tag is null)
            return "";
        var name = (_map.Data.RulingParties.TryGetValue(tag, out var party) ? _localisation.Text($"{tag}_{party}") : null)
                   ?? _localisation.Text(tag);
        return name is null ? tag : $"{name} ({tag})";
    }

    private MapStateViewModel Details(StateInfo state)
    {
        string Text(string key) => _localisation.Text(key) ?? key;
        var references = _map.References.TryGetValue(state.Id, out var found) ? found : [];
        return new MapStateViewModel(state)
        {
            Name = StateName(state),
            Owner = state.Owner is null ? "—" : CountryName(state.Owner),
            Cores = state.Cores.Count == 0 ? "—" : string.Join(", ", state.Cores.Select(CountryName)),
            Claims = state.Claims.Count == 0 ? "—" : string.Join(", ", state.Claims.Select(CountryName)),
            Category = state.Category is null ? "—" : Text(state.Category),
            Buildings = state.Buildings.OrderByDescending(b => b.Value).Select(b => $"{Text(b.Key)}: {b.Value}").ToList(),
            Resources = state.Resources.Where(r => r.Value != 0)
                .Select(r => $"{_localisation.Text("state_resource_" + r.Key) ?? r.Key}: {r.Value:0.##}").ToList(),
            VictoryPoints = state.VictoryPoints
                .Select(vp => $"{_localisation.Text($"VICTORY_POINTS_{vp.Province}") ?? $"провінція {vp.Province}"}: {vp.Points:0.##}")
                .ToList(),
            File = state.IsMod ? Path.GetRelativePath(Path.GetDirectoryName(FilePath)!, state.FullPath).Replace('\\', '/') : "гра: " + Path.GetFileName(state.FullPath),
            OpenCode = state.IsMod ? new RelayCommand(() => _openFile(state.FullPath, state.Position)) : null,
            References = references
                .Where(Matches)
                .OrderBy(r => r.Reference.Action)
                .Select(r => new MapReferenceViewModel(r, ActionTitles[(int)r.Reference.Action] + " · " + r.Reference.Key,
                    r.Reference.Usage.Owner is { } owner ? $"{_localisation.Text(owner.Id) ?? owner.Id}" : Path.GetFileName(r.File),
                    new RelayCommand(() =>
                    {
                        if (r.Reference.Usage.Owner is { } entity)
                            _openEntity(entity.Kind, entity.Id, r.File);
                        else
                            _openFile(r.File, r.Reference.Usage.Start);
                    }),
                    new RelayCommand(() => _openFile(r.File, r.Reference.Usage.Start))))
                .ToList(),
        };
    }

    public override void ReloadFromDisk()
    {
    }

    public override void Discard()
    {
        StateEditor?.Discard();
        _map.Changed -= OnSourceChanged;
        _localisation.Changed -= OnSourceChanged;
    }
}
