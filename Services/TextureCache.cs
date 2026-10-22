using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HoI4ModBuilder.Game.Imaging;

namespace HoI4ModBuilder.Services;

/// <param name="Image">The bitmap, or null when loading failed.</param>
/// <param name="Format">Source format, e.g. "DXT5".</param>
/// <param name="Error">Why loading failed.</param>
public sealed record TextureResult(Bitmap? Image, int Width, int Height, string? Format, string? Error);

/// <summary>
/// Decodes textures off the UI thread and keeps recently used ones, up to a memory budget.
/// Concurrent requests for the same file share one decode.
/// </summary>
public sealed class TextureCache
{
    private const long MaxBytes = 256L * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recency = new(); // most recent first
    private long _bytes;

    public Task<TextureResult> GetAsync(string fullPath)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(fullPath, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return node.Value.Task;
            }

            var entry = new Entry(fullPath, Task.Run(() => Load(fullPath)));
            _entries[fullPath] = _recency.AddFirst(entry);
            entry.Task.ContinueWith(t => Account(entry, t.Result), TaskScheduler.Default);
            return entry.Task;
        }
    }

    /// <summary>Forgets a file so the next request decodes it again (it changed on disk).</summary>
    public void Invalidate(string fullPath)
    {
        lock (_lock)
        {
            if (_entries.Remove(fullPath, out var node))
            {
                _recency.Remove(node);
                _bytes -= node.Value.Bytes;
            }
        }
    }

    private void Account(Entry entry, TextureResult result)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(entry.Path, out var node) || node.Value != entry)
                return; // invalidated while loading
            entry.Bytes = (long)result.Width * result.Height * 4;
            _bytes += entry.Bytes;

            // Evict least recently used, but never the entry just added.
            while (_bytes > MaxBytes && _recency.Last is { } last && last.Value != entry)
            {
                _recency.RemoveLast();
                _entries.Remove(last.Value.Path);
                _bytes -= last.Value.Bytes;
            }
        }
    }

    private static TextureResult Load(string path)
    {
        try
        {
            if (TextureDecoder.StandardExtensions.Contains(Path.GetExtension(path)))
            {
                var bitmap = new Bitmap(path);
                return new TextureResult(bitmap, bitmap.PixelSize.Width, bitmap.PixelSize.Height,
                    Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), null);
            }

            var image = TextureDecoder.Decode(File.ReadAllBytes(path), path);
            return new TextureResult(ToBitmap(image), image.Width, image.Height, image.Format, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Trace.WriteLine($"Texture {path}: {ex.Message}");
            return new TextureResult(null, 0, 0, null, ex.Message);
        }
    }

    private static WriteableBitmap ToBitmap(DecodedImage image)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        var rowBytes = image.Width * 4;
        for (var y = 0; y < image.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                image.Pixels, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
        }
        return bitmap;
    }

    private sealed class Entry(string path, Task<TextureResult> task)
    {
        public string Path { get; } = path;
        public Task<TextureResult> Task { get; } = task;
        public long Bytes { get; set; }
    }
}
