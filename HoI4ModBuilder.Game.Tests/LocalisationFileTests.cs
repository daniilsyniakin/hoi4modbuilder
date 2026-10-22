using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Game.Localisation;

namespace HoI4ModBuilder.Game.Tests;

public sealed class LocalisationFileTests
{
    private const string File = "﻿l_english:\r\n UKR_a:0 \"Old name\" # comment\r\n UKR_a_desc:1 \"Desc\"\r\n";

    [Fact]
    public void ReplacesOnlyTheValue()
    {
        var edit = LocalisationFile.SetValue(File, "UKR_a", "New \"name\"")!.Value;

        Assert.Equal("﻿l_english:\r\n UKR_a:0 \"New \\\"name\\\"\" # comment\r\n UKR_a_desc:1 \"Desc\"\r\n",
            ScriptEdits.Apply(File, [edit]));
    }

    [Fact]
    public void MatchesWholeKeys()
    {
        Assert.NotNull(LocalisationFile.FindValue(File, "UKR_a_desc"));
        Assert.Null(LocalisationFile.SetValue(File, "UKR", "x"));
    }

    [Fact]
    public void AppendsNewKeyAsLastLine()
    {
        var result = ScriptEdits.Apply(File, [LocalisationFile.Append(File, "UKR_b", "Two\nlines")]);

        // The line break is stored escaped, the way the game's files write it.
        Assert.EndsWith(" UKR_a_desc:1 \"Desc\"\r\n UKR_b:0 \"Two\\nlines\"\r\n", result);
        Assert.Equal("Two\\nlines", LocalisationIndex.Parse(result).Last().Value);
    }

    [Fact]
    public void AppendsAfterAMissingFinalLineBreak()
    {
        Assert.Equal("l_english:\n a:0 \"x\"\n b:0 \"y\"\n",
            ScriptEdits.Apply("l_english:\n a:0 \"x\"", [LocalisationFile.Append("l_english:\n a:0 \"x\"", "b", "y")]));
    }

    [Fact]
    public void NewFileIsReadableByTheIndex()
    {
        var text = LocalisationFile.NewFile("russian", "UKR_a", "Назва");

        Assert.Equal([("UKR_a", "Назва")], LocalisationIndex.Parse(text));
        Assert.StartsWith("l_russian:", text);
    }

    [Fact]
    public void EscapingRoundTrips()
    {
        const string value = "Say \"hi\"\nnext line";

        Assert.Equal(value, LocalisationFile.Unescape(LocalisationFile.Escape(value)));
    }

    [Fact]
    public void CountsKeysByPrefix() => Assert.Equal(2, LocalisationFile.CountKeysWithPrefix(File, "UKR_"));
}
