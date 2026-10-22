using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Localisation;

namespace HoI4ModBuilder.Game.Tests;

public sealed class LocalisationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hoi4mb-loc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void ParsesEntries()
    {
        const string text = "﻿l_english:\n # comment\n UKR_a:0 \"Independence\"\n UKR_b: \"Say \\\"hi\\\"\" # note\n broken line\n";

        Assert.Equal([("UKR_a", "Independence"), ("UKR_b", "Say \"hi\"")], LocalisationIndex.Parse(text));
    }

    [Fact]
    public void CleansColourCodes() =>
        Assert.Equal("Integrate Kyiv\nnow", LocalisationIndex.Clean("Integrate §YKyiv§!\nnow"));

    [Fact]
    public void ModOverridesGameAndOtherLanguagesAreAFallback()
    {
        Write("game/localisation/english/a_l_english.yml", "l_english:\n A:0 \"game A\"\n B:0 \"game B\"\n");
        Write("game/localisation/russian/a_l_russian.yml", "l_russian:\n C:0 \"ignored\"\n");
        Write("mod/localisation/english/m_l_english.yml", "l_english:\n A:0 \"mod A\"\n");
        Write("mod/localisation/russian/m_l_russian.yml", "l_russian:\n A:0 \"мод А\"\n D:0 \"мод Д\"\n");

        var index = LocalisationIndex.Load(new GameFileSystem(Path.Combine(_root, "game"), Path.Combine(_root, "mod")));

        Assert.Equal("mod A", index.Find("A"));
        Assert.Equal("game B", index.Find("b"));
        Assert.Null(index.Find("C"));
        Assert.Equal("мод Д", index.Find("D"));
        Assert.True(index.IsFallback("D"));
        Assert.False(index.IsFallback("A"));
    }
}
