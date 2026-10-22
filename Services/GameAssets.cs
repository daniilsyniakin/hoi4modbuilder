using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Imaging;
using HoI4ModBuilder.Game.Sprites;

namespace HoI4ModBuilder.Services;

/// <summary>
/// The game's graphics as seen with the mod loaded: the merged file system, the sprite catalog
/// and decoded textures. One instance per open mod; it follows changes to the mod's files.
/// </summary>
public sealed class GameAssets
{
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _reloadTimer;

    public GameAssets(string? gamePath, string modRoot)
    {
        FileSystem = new GameFileSystem(GameLocator.IsGameDirectory(gamePath) ? gamePath : null, modRoot);
        _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
        _reloadTimer.Tick += async (_, _) =>
        {
            _reloadTimer.Stop();
            await LoadSpritesAsync();
        };
    }

    public GameFileSystem FileSystem { get; }

    public TextureCache Textures { get; } = new();

    public SpriteCatalog Sprites { get; private set; } = SpriteCatalog.Empty;

    /// <summary>Raised on the UI thread whenever <see cref="Sprites"/> is replaced.</summary>
    public event EventHandler? SpritesChanged;

    public async Task LoadSpritesAsync()
    {
        Sprites = await Task.Run(() => SpriteCatalog.Load(FileSystem));
        SpritesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called for any change inside the mod folder.</summary>
    public void OnFileChanged(string fullPath)
    {
        if (TextureDecoder.IsImage(fullPath))
        {
            Textures.Invalidate(fullPath);
        }
        else if (Path.GetExtension(fullPath).Equals(".gfx", StringComparison.OrdinalIgnoreCase))
        {
            // Saving a .gfx can fire several events; reload once they settle.
            _reloadTimer.Stop();
            _reloadTimer.Start();
        }
    }

    /// <summary>The image a sprite shows: its texture, cut to the first frame for multi-frame sprites.</summary>
    public async Task<SpriteImage?> LoadSpriteAsync(SpriteDefinition sprite)
    {
        if (sprite.TextureFile is null || FileSystem.ResolveTexture(sprite.TextureFile) is not { } file)
            return null;

        var texture = await Textures.GetAsync(file.FullPath);
        if (texture.Image is not { } bitmap)
            return null;

        IImage image = bitmap;
        var width = texture.Width;
        if (sprite.Frames > 1 && texture.Width >= sprite.Frames)
        {
            width = texture.Width / sprite.Frames;
            image = new CroppedBitmap(bitmap, new PixelRect(0, 0, width, texture.Height));
        }
        return new SpriteImage(image, width, texture.Height, texture, file);
    }

    /// <summary>
    /// What a script points at as a picture: a sprite name (<c>GFX_goal_…</c>) or a texture path
    /// (<c>gfx/leaders/…</c>). Null when it can't be found or decoded.
    /// </summary>
    public async Task<IImage?> LoadPictureAsync(string? spriteOrPath)
    {
        if (string.IsNullOrWhiteSpace(spriteOrPath))
            return null;
        if (Sprites.Find(spriteOrPath) is { } sprite)
            return (await LoadSpriteAsync(sprite))?.Image;
        if (spriteOrPath.Contains('/') || spriteOrPath.Contains('\\'))
            return (await LoadTextureAsync(spriteOrPath))?.Image;
        return null;
    }

    public async Task<TextureResult?> LoadTextureAsync(string relativePath) =>
        FileSystem.ResolveTexture(relativePath) is { } file ? await Textures.GetAsync(file.FullPath) : null;
}

/// <param name="Image">What to draw (a single frame for multi-frame sprites).</param>
/// <param name="Width">Width of one frame in pixels.</param>
public sealed record SpriteImage(IImage Image, int Width, int Height, TextureResult Texture, VirtualFile File);
