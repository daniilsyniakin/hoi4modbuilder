using System.Diagnostics;
using System.Text.Json;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Imaging;
using HoI4ModBuilder.Game.Sprites;
using Xunit.Abstractions;

namespace HoI4ModBuilder.Game.Tests;

/// <summary>
/// Runs the decoders and the sprite catalog against an installed Hearts of Iron IV. Decoding all
/// 4.7 GB of textures takes too long, so up to <see cref="SamplePerFormat"/> files of every
/// distinct format are decoded. Without the game the tests pass vacuously and say so.
/// </summary>
[Trait("Category", "Corpus")]
public class GameGraphicsCorpusTests(ITestOutputHelper output)
{
    private const int SamplePerFormat = 150;

    [Fact]
    public void EveryTextureFormatInTheGameDecodes()
    {
        if (FindGameDirectory() is not { } game)
        {
            output.WriteLine("Hearts of Iron IV not found; set HOI4_GAME_DIR to run this test.");
            return;
        }

        var byFormat = Directory.EnumerateFiles(game, "*.*", SearchOption.AllDirectories)
            .Where(f => TextureDecoder.DecodedExtensions.Contains(Path.GetExtension(f)))
            .GroupBy(FormatKey)
            .ToList();

        var failures = new List<string>();
        var decoded = 0;
        var stopwatch = Stopwatch.StartNew();
        foreach (var group in byFormat.OrderByDescending(g => g.Count()))
        {
            var sample = group.Take(SamplePerFormat).ToList();
            foreach (var file in sample)
            {
                try
                {
                    var image = TextureDecoder.Decode(File.ReadAllBytes(file), file);
                    Assert.Equal(image.Width * image.Height * 4, image.Pixels.Length);
                    decoded++;
                }
                catch (InvalidDataException ex)
                {
                    failures.Add($"{Path.GetRelativePath(game, file)}: {ex.Message}");
                }
            }
            output.WriteLine($"{group.Count(),6} files  {group.Key}");
        }

        output.WriteLine($"Decoded {decoded} sampled textures in {stopwatch.Elapsed.TotalSeconds:F1} s; failures: {failures.Count}");
        foreach (var failure in failures.Take(30))
            output.WriteLine("  " + failure);
        Assert.Empty(failures);
    }

    [Fact]
    public void SpriteCatalogOfTheGame()
    {
        if (FindGameDirectory() is not { } game)
        {
            output.WriteLine("Hearts of Iron IV not found; set HOI4_GAME_DIR to run this test.");
            return;
        }

        var fs = new GameFileSystem(game, null);
        var stopwatch = Stopwatch.StartNew();
        var catalog = SpriteCatalog.Load(fs);
        output.WriteLine($"{catalog.Count} sprites loaded in {stopwatch.ElapsedMilliseconds} ms");

        foreach (var kind in catalog.All.GroupBy(s => s.Kind).OrderByDescending(g => g.Count()))
            output.WriteLine($"  {kind.Key}: {kind.Count()}");

        var missing = catalog.All
            .Where(s => s.TextureFile is not null && fs.ResolveTexture(s.TextureFile) is null)
            .ToList();
        output.WriteLine($"Sprites whose texture doesn't exist in the game files: {missing.Count}");
        foreach (var sprite in missing.Take(15))
            output.WriteLine($"  {sprite.Name} → {sprite.TextureFile} ({sprite.SourceFile})");

        Assert.True(catalog.Count > 10_000);
        Assert.NotNull(catalog.Find("GFX_goal_generic_production"));
        // Vanilla has dangling references (textures that exist nowhere on disk); anything more
        // than that means resolution is broken.
        Assert.True(missing.Count < catalog.Count / 50, $"{missing.Count} unresolved textures");
    }

    /// <summary>"DDS DXT5", "DDS RGB 32-bit masks…", "TGA type 2 32-bit"… read from headers only.</summary>
    private static string FormatKey(string file)
    {
        var header = new byte[148];
        using (var stream = File.OpenRead(file))
            stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        if (DdsDecoder.IsDds(header))
        {
            var flags = BitConverter.ToUInt32(header, 80);
            return (flags & 4) != 0
                ? $"DDS {System.Text.Encoding.ASCII.GetString(header, 84, 4)}"
                : $"DDS {BitConverter.ToUInt32(header, 88)}-bit flags 0x{flags:X}";
        }
        return $"TGA type {header[2]} {header[16]}-bit desc 0x{header[17]:X2}";
    }

    internal static string? FindGameDirectory()
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
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && Directory.Exists(Path.Combine(c, "interface")));
    }
}
