using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Logs;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

/// <summary>A piece of a log message: plain text, or a link to a mod file (at a line) or a mod entity.</summary>
public sealed class LogSegmentViewModel(string text, IRelayCommand? open = null, string? toolTip = null)
{
    public string Text { get; } = text;

    public IRelayCommand? Open { get; } = open;

    public bool IsLink => Open is not null;

    public string? ToolTip { get; } = toolTip;
}

/// <summary>One distinct message, with how often it occurred.</summary>
public sealed class LogMessageViewModel(IReadOnlyList<LogSegmentViewModel> segments, int count, int line, bool isMod)
{
    /// <summary>Mentions one of the mod's files or entities.</summary>
    public bool IsMod { get; } = isMod;

    public IReadOnlyList<LogSegmentViewModel> Segments { get; } = segments;

    public int Count { get; } = count;

    public bool IsRepeated => Count > 1;

    public string CountText => $"×{Count}";

    /// <summary>The first line of error.log it appears on.</summary>
    public int Line { get; } = line;
}

/// <summary>Messages that say the same thing (about different names), from the same engine source.</summary>
public sealed partial class LogGroupViewModel(string key, string pattern, string source, bool isMod, IReadOnlyList<LogMessageViewModel> messages)
    : ViewModelBase
{
    public string Key { get; } = key;

    public string Pattern { get; } = pattern;

    /// <summary>The engine file that reported it (<c>effectimplementation.cpp</c>), without the line.</summary>
    public string Source { get; } = source;

    /// <summary>About the open mod (its files or entities) rather than the game or other mods.</summary>
    public bool IsMod { get; } = isMod;

    public IReadOnlyList<LogMessageViewModel> Messages { get; } = messages;

    /// <summary>One distinct message: shown as is. Several: the pattern, expandable.</summary>
    public bool IsSingle => Messages.Count == 1;

    public LogMessageViewModel First => Messages[0];

    public int Count { get; } = messages.Sum(m => m.Count);

    public string CountText => $"×{Count}";

    public bool IsRepeated => Count > 1;

    public string VariantsText => $"{Messages.Count} різних";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

/// <summary>
/// The game's error.log, made readable: grouped repeats, links to the mod's files and entities,
/// mod problems apart from the game's own, a filter.
/// </summary>
public sealed partial class GameLogViewModel : ViewModelBase
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);

    private readonly Func<string> _logPath;
    private readonly ContentIndex _content;
    private readonly string _modRoot;
    private readonly Action<string, int?> _openFile;
    private readonly Action<ContentKind, string> _openEntity;
    private readonly DispatcherTimer _poll;
    private readonly HashSet<string> _expanded = [];
    private List<LogGroupViewModel> _all = [];
    private (DateTime Written, long Length) _seen;

    public GameLogViewModel(Func<string> logPath, ContentIndex content, string modRoot,
        Action<string, int?> openFile, Action<ContentKind, string> openEntity)
    {
        _logPath = logPath;
        _content = content;
        _modRoot = modRoot;
        _openFile = openFile;
        _openEntity = openEntity;
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => ReloadIfChanged();
        _poll.Start();
        content.Changed += (_, _) => Reload();
    }

    public ObservableCollection<LogGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial bool ShowMod { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowGame { get; set; } = true;

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial int ModCount { get; private set; }

    [ObservableProperty]
    public partial int GameCount { get; private set; }

    /// <summary>"error.log · 21:57", or that there is none yet.</summary>
    [ObservableProperty]
    public partial string FileInfo { get; private set; } = "";

    public bool IsEmpty => Groups.Count == 0;

    partial void OnShowModChanged(bool value) => Filter();

    partial void OnShowGameChanged(bool value) => Filter();

    partial void OnSearchChanged(string? value) => Filter();

    private void ReloadIfChanged()
    {
        var file = new FileInfo(_logPath());
        var stamp = file.Exists ? (file.LastWriteTimeUtc, file.Length) : default;
        if (stamp != _seen)
            Reload();
    }

    /// <summary>Re-reads error.log (the game keeps it open and appends while running).</summary>
    public void Reload()
    {
        var path = _logPath();
        var file = new FileInfo(path);
        _seen = file.Exists ? (file.LastWriteTimeUtc, file.Length) : default;
        if (!file.Exists)
        {
            FileInfo = "error.log ще немає";
            _all = [];
            Filter();
            return;
        }

        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return; // being rewritten; the next tick tries again
        }
        FileInfo = $"error.log · {file.LastWriteTime:HH:mm:ss}";

        var modIds = _content.Files.Values.SelectMany(ContentParser.Flatten)
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.Ordinal);

        _all = GameLog.Parse(text)
            .GroupBy(e => (Source: SourceFile(e.Source), Pattern: GameLog.Pattern(e.Message)))
            .Select(group =>
            {
                var messages = group
                    .GroupBy(e => e.Message)
                    .Select(m => Message(m.Key, m.Count(), m.First().LineNumber, modIds))
                    .ToList();
                var isMod = messages.Any(m => m.IsMod);
                var key = group.Key.Source + "|" + group.Key.Pattern;
                return new LogGroupViewModel(key, group.Key.Pattern, group.Key.Source, isMod, messages)
                {
                    IsExpanded = _expanded.Contains(key),
                };
            })
            // The mod's own problems first, then the most frequent.
            .OrderByDescending(g => g.IsMod)
            .ThenByDescending(g => g.Count)
            .ToList();
        foreach (var group in _all)
            group.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(LogGroupViewModel.IsExpanded) && sender is LogGroupViewModel g)
                {
                    if (g.IsExpanded)
                        _expanded.Add(g.Key);
                    else
                        _expanded.Remove(g.Key);
                }
            };
        ModCount = _all.Where(g => g.IsMod).Sum(g => g.Count);
        GameCount = _all.Where(g => !g.IsMod).Sum(g => g.Count);
        Filter();
    }

    private void Filter()
    {
        var words = (Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Groups.Clear();
        foreach (var group in _all)
        {
            if (group.IsMod ? !ShowMod : !ShowGame)
                continue;
            if (words.Length > 0 && !words.All(w => group.Source.Contains(w, StringComparison.OrdinalIgnoreCase)
                    || group.Messages.Any(m => m.Segments.Any(s => s.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))))
                continue;
            Groups.Add(group);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>A message cut into text and links: mod files open at their line, mod ids open their editor.</summary>
    private LogMessageViewModel Message(string text, int count, int line, IReadOnlyDictionary<string, ContentKind> modIds)
    {
        var isMod = false;
        var segments = new List<LogSegmentViewModel>();
        var position = 0;
        foreach (var link in GameLog.FindLinks(text, modIds.ContainsKey))
        {
            if (link.Start > position)
                segments.Add(new LogSegmentViewModel(text[position..link.Start]));
            var shown = text.Substring(link.Start, link.Length);
            if (link.Kind == LogLinkKind.Entity && modIds.TryGetValue(link.Value, out var kind))
            {
                segments.Add(new LogSegmentViewModel(shown, new RelayCommand(() => _openEntity(kind, link.Value))));
                isMod = true;
            }
            else if (ModFile(link.Value) is { } full)
            {
                var at = link.Line is { } l ? $" · рядок {l}" : "";
                segments.Add(new LogSegmentViewModel(shown, new RelayCommand(() => _openFile(full, link.Line)), link.Value + at));
                isMod = true;
            }
            else
            {
                segments.Add(new LogSegmentViewModel(shown));
            }
            position = link.Start + link.Length;
        }
        if (position < text.Length)
            segments.Add(new LogSegmentViewModel(text[position..]));
        return new LogMessageViewModel(segments, count, line, isMod);
    }

    /// <summary>The mod's file a log path refers to, if the mod has it.</summary>
    private string? ModFile(string relative)
    {
        var full = Path.Combine(_modRoot, relative);
        return File.Exists(full) ? full : null;
    }

    private static string SourceFile(string source) =>
        source.LastIndexOf(':') is > 0 and var colon ? source[..colon] : source;

    [RelayCommand]
    private void Refresh() => Reload();
}
