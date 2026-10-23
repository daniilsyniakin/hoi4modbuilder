using HoI4ModBuilder.Game.Logs;

namespace HoI4ModBuilder.Game.Tests;

public sealed class GameLogTests
{
    private const string Log = """
        [21:57:27][no_game_date][persistent.cpp:67]: Error: "Unexpected token: thumbnail, near line: 9" in file: "mod/Teefia.mod" near line: 9
        [21:57:34][no_game_date][assetfactory_audio.cpp:467]: Could not load sound file 'sound/menu/sfx_ui_sd_module_turrent_01.wav' in  file: sound/sound.asset line: 730
        [21:57:42][no_game_date][pdx_entity.cpp:2172]: Duplicate of HOL_cavalry_entity added to entity system
        [21:57:42][no_game_date][pdx_entity.cpp:2172]: Duplicate of INS_cavalry_entity added to entity system
        [10:01:02][1936.1.1.12][effectimplementation.cpp:4021]: Invalid country tag in common/national_focus/ukraine.txt
          continued on a second line
        """;

    [Fact]
    public void ParsesEntriesAndContinuationLines()
    {
        var entries = GameLog.Parse(Log).ToList();

        Assert.Equal(5, entries.Count);
        Assert.Equal(("21:57:27", "no_game_date", "persistent.cpp:67"), (entries[0].Time, entries[0].GameDate, entries[0].Source));
        Assert.StartsWith("Error: \"Unexpected token", entries[0].Message);
        Assert.Equal("1936.1.1.12", entries[4].GameDate);
        Assert.EndsWith("\ncontinued on a second line", entries[4].Message);
    }

    [Fact]
    public void FindsFilesWithTheirLines()
    {
        var entries = GameLog.Parse(Log).ToList();

        var descriptor = Assert.Single(GameLog.FindLinks(entries[0].Message, _ => false));
        Assert.Equal((LogLinkKind.File, "mod/Teefia.mod", 9), (descriptor.Kind, descriptor.Value, descriptor.Line));

        var sound = GameLog.FindLinks(entries[1].Message, _ => false);
        Assert.Equal(["sound/menu/sfx_ui_sd_module_turrent_01.wav", "sound/sound.asset"], sound.Select(l => l.Value));
        Assert.Equal(730, sound[1].Line);
        Assert.Equal("sound/sound.asset", entries[1].Message.Substring(sound[1].Start, sound[1].Length));
    }

    [Fact]
    public void LinksIdsTheModDefines()
    {
        const string message = "Unknown effect in focus UKR_army_reform, event ukr.12: has_war";

        var links = GameLog.FindLinks(message, id => id is "UKR_army_reform" or "ukr.12");

        Assert.Equal(["UKR_army_reform", "ukr.12"], links.Select(l => l.Value));
        Assert.All(links, l => Assert.Equal(LogLinkKind.Entity, l.Kind));
        Assert.Equal("ukr.12", message.Substring(links[1].Start, links[1].Length));
    }

    [Fact]
    public void SimilarMessagesShareAPattern()
    {
        var entries = GameLog.Parse(Log).ToList();

        Assert.Equal(GameLog.Pattern(entries[2].Message), GameLog.Pattern(entries[3].Message));
        Assert.Equal("Duplicate of «…» added to entity system", GameLog.Pattern(entries[2].Message));
        Assert.NotEqual(GameLog.Pattern(entries[0].Message), GameLog.Pattern(entries[2].Message));
    }
}
