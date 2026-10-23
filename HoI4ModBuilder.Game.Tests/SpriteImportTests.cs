using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Sprites;

namespace HoI4ModBuilder.Game.Tests;

public sealed class SpriteImportTests
{
    [Fact]
    public void NewGfxFileDefinesTheSprite()
    {
        var text = ScriptEdits.Apply("", SpriteImport.AddSpriteType("", "GFX_focus_ukr_army", "gfx/interface/goals/ukr_army.dds"));

        var sprite = Assert.Single(SpriteCatalog.Parse(SyntaxTree.Parse(text), "interface/mod_goals.gfx", FileLayer.Mod));
        Assert.Equal(("GFX_focus_ukr_army", "gfx/interface/goals/ukr_army.dds"), (sprite.Name, sprite.TextureFile));
    }

    [Fact]
    public void AddsToTheExistingSpriteTypesBlock()
    {
        const string text = "spriteTypes = {\r\n\tspriteType = {\r\n\t\tname = \"GFX_focus_a\"\r\n\t\ttexturefile = \"a.dds\"\r\n\t}\r\n}\r\n";

        var result = ScriptEdits.Apply(text, SpriteImport.AddSpriteType(text, "GFX_focus_b", "b.dds"));

        Assert.Equal(["GFX_focus_a", "GFX_focus_b"], SpriteCatalog.Parse(SyntaxTree.Parse(result), "x.gfx", FileLayer.Mod).Select(s => s.Name));
        Assert.Contains("\r\n\tspriteType = {\r\n\t\tname = \"GFX_focus_b\"\r\n\t\ttexturefile = \"b.dds\"\r\n\t}\r\n}", result);
    }

    [Theory]
    [InlineData("My Army (2).png", "my_army_2")]
    [InlineData("Знамено.jpg", "picture")]
    [InlineData("UKR-flag_final.PNG", "ukr_flag_final")]
    public void NamesComeFromTheFileName(string file, string name) => Assert.Equal(name, SpriteImport.NameFrom(file));

    [Fact]
    public void CoverCropKeepsTheCentreInTheTargetsProportions()
    {
        // A wide 400×100 picture into a 100×88 focus: a 114×100 slice from the middle.
        Assert.Equal((143, 0, 114, 100), SpriteImport.CoverCrop(400, 100, 100, 88));
        // A tall one keeps the full width.
        Assert.Equal((0, 132, 200, 176), SpriteImport.CoverCrop(200, 440, 100, 88));
    }

    [Fact]
    public void KindFollowsThePickersPrefixes()
    {
        Assert.Equal(SpriteImportKind.Focus, SpriteImport.KindFor(["GFX_goal_", "GFX_focus_"]));
        Assert.Equal(SpriteImportKind.NewsPicture, SpriteImport.KindFor(["GFX_news_event_", "GFX_report_event_"]));
        Assert.Null(SpriteImport.KindFor(["GFX_something_"]));
    }
}
