using HoI4ModBuilder.Models;

namespace HoI4ModBuilder.Services;

/// <summary>Creates the folder structure and descriptor files for a brand-new mod.</summary>
public static class ModFactory
{
    /// <param name="location">Folder that will contain the mod folder.</param>
    /// <param name="folderName">Name of the mod folder (also used for the launcher's <c>.mod</c> file).</param>
    /// <param name="launcherModsDirectory">Folder the Paradox launcher scans for <c>.mod</c> files.</param>
    /// <returns>Absolute path to the created mod folder.</returns>
    public static string Create(ModDescriptor descriptor, string location, string folderName, string launcherModsDirectory)
    {
        var modPath = Path.GetFullPath(Path.Combine(location, folderName));
        var launcherFile = Path.Combine(launcherModsDirectory, folderName + ".mod");

        if (Directory.Exists(modPath) && Directory.EnumerateFileSystemEntries(modPath).Any())
            throw new InvalidModException($"Папка «{modPath}» вже існує і не порожня.");
        if (File.Exists(launcherFile))
            throw new InvalidModException(
                $"Файл «{launcherFile}» вже існує — лаунчер вже знає мод з такою назвою папки.");

        Directory.CreateDirectory(modPath);
        File.WriteAllText(Path.Combine(modPath, ModProject.DescriptorFileName), descriptor.Serialize());

        Directory.CreateDirectory(launcherModsDirectory);
        File.WriteAllText(launcherFile, descriptor.Serialize(modPath));

        return modPath;
    }
}
