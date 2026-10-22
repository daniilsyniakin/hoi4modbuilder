using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Sprites;

namespace HoI4ModBuilder.Game.Tests;

public sealed class FileSystemAndSpriteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hoi4mb-tests-" + Guid.NewGuid().ToString("N"));

    private string Game => Path.Combine(_root, "game");
    private string Mod => Path.Combine(_root, "mod");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Write(string root, string relative, string content = "")
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void ModFileReplacesGameFile()
    {
        Write(Game, "interface/goals.gfx", "game");
        Write(Mod, "interface/goals.gfx", "mod");
        Write(Game, "interface/other.gfx", "game");

        var fs = new GameFileSystem(Game, Mod);

        Assert.Equal(FileLayer.Mod, fs.Resolve("interface/goals.gfx")!.Layer);
        Assert.Equal(FileLayer.Game, fs.Resolve(@"interface\other.gfx")!.Layer);
        Assert.Null(fs.Resolve("interface/missing.gfx"));

        var files = fs.EnumerateFiles("interface", "*.gfx");
        Assert.Equal(["interface/goals.gfx", "interface/other.gfx"], files.Select(f => f.RelativePath));
        Assert.Equal(FileLayer.Mod, files[0].Layer);
    }

    [Fact]
    public void DlcFoldersAreLayersBetweenGameAndMod()
    {
        Write(Game, "gfx/a.dds");
        Write(Game, "dlc/dlc001_pack/gfx/a.dds");
        Write(Game, "dlc/dlc001_pack/interface/dlc.gfx");
        Write(Game, "gfx/b.dds");
        Write(Mod, "gfx/b.dds");

        var fs = new GameFileSystem(Game, Mod);

        Assert.Equal(FileLayer.Dlc, fs.Resolve("gfx/a.dds")!.Layer);
        Assert.Equal(FileLayer.Mod, fs.Resolve("gfx/b.dds")!.Layer);
        Assert.Equal("interface/dlc.gfx", Assert.Single(fs.EnumerateFiles("interface", "*.gfx")).RelativePath);
        Assert.Equal("gfx/a.dds", fs.ToRelative(Path.Combine(Game, "dlc", "dlc001_pack", "gfx", "a.dds")));
    }

    [Fact]
    public void TextureResolutionTriesOtherExtensions()
    {
        Write(Game, "gfx/flags/UKR.tga");
        var fs = new GameFileSystem(Game, null);

        Assert.EndsWith("UKR.tga", fs.ResolveTexture("gfx/flags/UKR.dds")!.FullPath);
    }

    [Fact]
    public void ToRelativeFindsTheLayer()
    {
        Write(Mod, "gfx/a.dds");
        var fs = new GameFileSystem(Game, Mod);

        Assert.Equal("gfx/a.dds", fs.ToRelative(Path.Combine(Mod, "gfx", "a.dds")));
        Assert.Null(fs.ToRelative(Path.GetTempPath()));
    }

    [Fact]
    public void ParsesAllSpriteKindsCaseInsensitively()
    {
        var tree = SyntaxTree.Parse("""
            spriteTypes = {
                SpriteType = { name = "GFX_goal_a" texturefile = "gfx/interface/goals/a.dds" }
                spriteType = { name = GFX_button textureFile = "gfx/button.dds" noOfFrames = 3 }
                frameAnimatedSpriteType = { name = "GFX_glow" texturefile = "gfx/glow.dds" noOfFrames = 2 }
                corneredTileSpriteType = { name = "GFX_tile" textureFile = "gfx/tile.dds" size = { x = 8 y = 8 } }
                maskedShieldType = { name = "GFX_shield" textureFile1 = "gfx/shield.dds" textureFile2 = "gfx/mask.dds" }
                unknownType = { name = "GFX_ignored" }
                spriteType = { texturefile = "gfx/nameless.dds" }
            }
            objectTypes = { pdxmesh = { name = "not_a_sprite" } }
            """);

        var sprites = SpriteCatalog.Parse(tree, "interface/test.gfx", FileLayer.Game).ToList();

        Assert.Equal(["GFX_goal_a", "GFX_button", "GFX_glow", "GFX_tile", "GFX_shield"], sprites.Select(s => s.Name));
        Assert.Equal(3, sprites[1].Frames);
        Assert.Equal(SpriteKind.FrameAnimated, sprites[2].Kind);
        Assert.Equal("gfx/shield.dds", sprites[4].TextureFile);
        Assert.Equal(SpriteKind.CorneredTile, sprites[3].Kind);
    }

    [Fact]
    public void ModSpritesOverrideGameSprites()
    {
        Write(Game, "interface/goals.gfx", "spriteTypes = { spriteType = { name = GFX_a texturefile = game.dds } spriteType = { name = GFX_b texturefile = b.dds } }");
        Write(Mod, "interface/zzz_mod.gfx", "spriteTypes = { spriteType = { name = GFX_a texturefile = mod.dds } }");

        var catalog = SpriteCatalog.Load(new GameFileSystem(Game, Mod));

        Assert.Equal(2, catalog.Count);
        Assert.Equal("mod.dds", catalog.Find("GFX_a")!.TextureFile);
        Assert.Equal(FileLayer.Mod, catalog.Find("gfx_a")!.Layer);
        Assert.Equal("b.dds", catalog.Find("GFX_b")!.TextureFile);
    }
}
