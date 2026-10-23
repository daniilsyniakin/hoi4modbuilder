using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Localisation;

namespace HoI4ModBuilder.Game.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public void PutsTheLanguagesOfAKeySideBySide()
    {
        var table = LocalisationTable.Read(
        [
            ("loc/russian/ukr_l_russian.yml", "\uFEFFl_russian:\n UKR_a:0 \"Реформа\"\n"),
            ("loc/english/ukr_l_english.yml", "l_english:\n UKR_a:0 \"Reform\"\n UKR_b:0 \"Say \\\"no\\\"\\nnow\"\n"),
            ("loc/notes.txt", "not localisation"),
        ]);

        Assert.Equal(["english", "russian"], table.Languages);
        Assert.Equal(["UKR_a", "UKR_b"], table.Keys);
        Assert.Equal("Реформа", table.Value("UKR_a", "russian"));
        Assert.Null(table.Value("UKR_b", "russian"));
        Assert.Equal("Say \\\"no\\\"\\nnow", table.Value("UKR_b", "english"));
        Assert.Equal("loc/english/ukr_l_english.yml", table.File("UKR_b", "english"));
    }

    [Fact]
    public void MirrorsAFileIntoAnotherLanguage()
    {
        Assert.Equal(Path.Combine("loc", "russian", "ukr_l_russian.yml"),
            LocalisationTable.Mirror(Path.Combine("loc", "english", "ukr_l_english.yml"), "english", "russian"));
        Assert.Equal(Path.Combine("loc", "ukr_l_german.yml"),
            LocalisationTable.Mirror(Path.Combine("loc", "ukr_l_english.yml"), "english", "german"));
    }

    [Fact]
    public void ReadsTechnologiesWithTheirConstants()
    {
        var techs = TechnologyCatalog.Read("""
            technologies = {
                @1936 = 2
                infantry_weapons = {
                    path = { leads_to_tech = infantry_weapons1 research_cost_coeff = 1 }
                    research_cost = 1.5
                    start_year = 1918
                    folder = { name = infantry_folder position = { x = 0 y = @1936 } }
                    categories = { infantry_weapons light_infantry }
                }
                infantry_weapons1 = { research_cost = @1936 start_year = 1936 }
            }
            """, "common/technologies/infantry.txt", "C:/game/common/technologies/infantry.txt", isMod: false).ToList();

        Assert.Equal(["infantry_weapons", "infantry_weapons1"], techs.Select(t => t.Id));
        Assert.Equal((1918, 1.5m, "infantry_folder"), (techs[0].StartYear, techs[0].ResearchCost, techs[0].Folder));
        Assert.Equal(["infantry_weapons", "light_infantry"], techs[0].Categories);
        Assert.Equal(["infantry_weapons1"], techs[0].LeadsTo);
        Assert.Equal(2m, techs[1].ResearchCost);
    }
}
