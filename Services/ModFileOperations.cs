using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace HoI4ModBuilder.Services;

/// <summary>Creating and deleting files in the mod folder from the project tree.</summary>
public static class ModFileOperations
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// Checks a name typed by the user. Like JetBrains IDEs, "events/my_events.txt" is allowed
    /// and creates the missing folders on the way.
    /// </summary>
    /// <returns>An error message, or null if <paramref name="name"/> can be created in <paramref name="folder"/>.</returns>
    public static string? ValidateNewName(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Вкажіть назву.";

        var segments = name.Split(Separators);
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment.Trim() is "." or "..")
                return "Назва не може містити порожніх частин чи «..».";
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return $"«{segment}» містить недопустимі символи.";
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                return "Назва не може закінчуватися пробілом чи крапкою.";
        }

        var target = Path.Combine(folder, Path.Combine(segments));
        if (File.Exists(target) || Directory.Exists(target))
            return "Файл чи папка з такою назвою вже існує.";
        return null;
    }

    /// <returns>Full path of the created file.</returns>
    public static string CreateFile(string folder, string name)
    {
        var path = Path.Combine(folder, Path.Combine(name.Split(Separators)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // The game only reads localisation files that start with a UTF-8 byte order mark.
        var content = Path.GetExtension(path).Equals(".yml", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetPreamble()
            : [];
        using (var stream = new FileStream(path, FileMode.CreateNew))
            stream.Write(content);
        return path;
    }

    /// <returns>Full path of the created folder.</returns>
    public static string CreateFolder(string folder, string name)
    {
        var path = Path.Combine(folder, Path.Combine(name.Split(Separators)));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>On Windows the item goes to the Recycle Bin, so a mistake can be undone.</summary>
    public static bool UsesRecycleBin => OperatingSystem.IsWindows();

    /// <exception cref="IOException">The item couldn't be deleted.</exception>
    public static void Delete(string path)
    {
        var isDirectory = Directory.Exists(path);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (isDirectory)
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (OperationCanceledException)
            {
                throw new IOException("Видалення скасовано.");
            }
            return;
        }

        if (isDirectory)
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);
    }

    /// <summary>Number of files inside a folder, for the confirmation message.</summary>
    public static int CountFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.AllDirectories).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
