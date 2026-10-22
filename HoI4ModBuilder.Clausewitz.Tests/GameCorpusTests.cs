using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace HoI4ModBuilder.Clausewitz.Tests;

/// <summary>
/// Parses every script of an installed Hearts of Iron IV. Round-trip failures fail the test;
/// diagnostics are only reported, since they show where the grammar and the game disagree.
/// The game is located via HOI4_GAME_DIR, the app's settings, or the default Steam path;
/// without it the test passes vacuously and says so in its output.
/// </summary>
[Trait("Category", "Corpus")]
public class GameCorpusTests(ITestOutputHelper output)
{
    private static readonly string[] ScriptExtensions = [".txt", ".gui", ".gfx", ".asset", ".mod"];

    [Fact]
    public void EveryGameScriptRoundTrips()
    {
        var gameDir = FindGameDirectory();
        if (gameDir is null)
        {
            output.WriteLine("Hearts of Iron IV not found; set HOI4_GAME_DIR to run this test.");
            return;
        }

        var files = Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories)
            .Where(f => ScriptExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var roundTripFailures = new List<string>();
        var filesWithDiagnostics = new List<(string File, string Text, SyntaxTree Tree)>();
        long totalChars = 0;
        var parseTime = new Stopwatch();
        var total = Stopwatch.StartNew();
        var largest = (File: "", Chars: 0, Time: TimeSpan.Zero);

        foreach (var file in files)
        {
            // Latin-1 maps every byte to exactly one char, so the check is byte-exact
            // regardless of the file's real encoding.
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(file));
            totalChars += text.Length;

            var before = parseTime.Elapsed;
            parseTime.Start();
            var tree = SyntaxTree.Parse(text);
            parseTime.Stop();
            if (text.Length > largest.Chars)
                largest = (file, text.Length, parseTime.Elapsed - before);

            if (TreeAssert.FindViolation(tree) is { } violation)
                roundTripFailures.Add($"{Relative(gameDir, file)}: {violation}");
            else if (tree.Diagnostics.Count > 0)
                filesWithDiagnostics.Add((file, text, tree));
        }
        total.Stop();

        output.WriteLine($"Game: {gameDir}");
        output.WriteLine($"{files.Count} files, {totalChars / 1024.0 / 1024.0:F1} MB.");
        output.WriteLine($"Parsing: {parseTime.Elapsed.TotalSeconds:F1} s; with invariant checks: {total.Elapsed.TotalSeconds:F1} s.");
        output.WriteLine($"Largest: {Relative(gameDir, largest.File)} ({largest.Chars / 1024} KB) parsed in {largest.Time.TotalMilliseconds:F0} ms.");
        output.WriteLine($"Round-trip failures: {roundTripFailures.Count}");
        output.WriteLine($"Files with diagnostics: {filesWithDiagnostics.Count}");

        var byCode = filesWithDiagnostics
            .SelectMany(f => f.Tree.Diagnostics.Select(d => (f.File, Diagnostic: d)))
            .GroupBy(x => x.Diagnostic.Code);
        foreach (var group in byCode.OrderByDescending(g => g.Count()))
        {
            var groupFiles = group.Select(x => Relative(gameDir, x.File)).Distinct().ToList();
            output.WriteLine($"  {group.Key}: {group.Count()} in {groupFiles.Count} files: {string.Join(", ", groupFiles.Take(6))}");
        }

        foreach (var (file, text, tree) in filesWithDiagnostics.Take(40))
        {
            var diagnostic = tree.Diagnostics[0];
            var (line, lineText) = LineAt(text, diagnostic.Span.Start);
            output.WriteLine($"{Relative(gameDir, file)}:{line}: {diagnostic.Code} {diagnostic.Message}");
            output.WriteLine($"    {lineText.Trim()}");
        }

        Assert.True(roundTripFailures.Count == 0, string.Join("\n", roundTripFailures.Take(20)));
    }

    private static string? FindGameDirectory()
    {
        var candidates = new List<string?> { Environment.GetEnvironmentVariable("HOI4_GAME_DIR") };

        var settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoI4ModBuilder", "settings.json");
        if (File.Exists(settings))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(settings));
            if (json.RootElement.TryGetProperty("GamePath", out var gamePath))
                candidates.Add(gamePath.GetString());
        }

        candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Hearts of Iron IV");
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && Directory.Exists(Path.Combine(c, "common")));
    }

    private static string Relative(string root, string file) => Path.GetRelativePath(root, file);

    private static (int Line, string Text) LineAt(string text, int position)
    {
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }
        var lineEnd = text.IndexOf('\n', lineStart);
        var lineText = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];
        return (line, lineText.Length > 160 ? lineText[..160] + "…" : lineText);
    }
}
