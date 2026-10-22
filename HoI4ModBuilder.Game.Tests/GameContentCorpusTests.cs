using System.Diagnostics;
using System.Text;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Game.Content;
using Xunit.Abstractions;

namespace HoI4ModBuilder.Game.Tests;

/// <summary>Reads the content folders of an installed Hearts of Iron IV. Passes vacuously without the game.</summary>
[Trait("Category", "Corpus")]
public class GameContentCorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void ContentOfTheGame()
    {
        if (GameGraphicsCorpusTests.FindGameDirectory() is not { } game)
        {
            output.WriteLine("Hearts of Iron IV not found; set HOI4_GAME_DIR to run this test.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var entries = new List<ContentEntry>();
        foreach (var folder in ContentParser.Folders)
        {
            var directory = Path.Combine(game, folder);
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.txt"))
            {
                var relative = Path.GetRelativePath(game, file);
                Assert.True(ContentParser.IsContentFile(relative), relative);
                var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                entries.AddRange(ContentParser.Parse(SyntaxTree.Parse(text), relative));
            }
        }
        output.WriteLine($"Parsed in {stopwatch.ElapsedMilliseconds} ms");

        var all = entries.Concat(entries.SelectMany(e => e.Children)).ToList();
        var counts = all.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (kind, count) in counts.OrderBy(c => c.Key))
            output.WriteLine($"  {kind}: {count}");

        Assert.True(counts.GetValueOrDefault(ContentKind.FocusTree) > 50);
        Assert.True(counts.GetValueOrDefault(ContentKind.Focus) > 5000);
        Assert.True(counts.GetValueOrDefault(ContentKind.Event) > 2000);
        Assert.True(counts.GetValueOrDefault(ContentKind.Idea) > 1000);
        Assert.True(counts.GetValueOrDefault(ContentKind.Decision) > 500);
        Assert.True(counts.GetValueOrDefault(ContentKind.Technology) > 300);
        Assert.True(counts.GetValueOrDefault(ContentKind.Character) > 1000);
        Assert.True(counts.GetValueOrDefault(ContentKind.Country) > 200);
        Assert.True(counts.GetValueOrDefault(ContentKind.State) > 800);
        Assert.Contains(all, e => e is { Kind: ContentKind.Country, Id: "UKR" });
    }
}
