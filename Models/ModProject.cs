namespace HoI4ModBuilder.Models;

/// <summary>A mod opened in the builder: its root folder and parsed descriptor.</summary>
public sealed class ModProject
{
    public const string DescriptorFileName = "descriptor.mod";

    public required string RootPath { get; init; }
    public required ModDescriptor Descriptor { get; init; }

    public string Name => string.IsNullOrWhiteSpace(Descriptor.Name)
        ? Path.GetFileName(RootPath)
        : Descriptor.Name;

    public static ModProject Load(string rootPath)
    {
        rootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(rootPath))
            throw new InvalidModException($"Папку «{rootPath}» не знайдено.");

        var descriptorPath = Path.Combine(rootPath, DescriptorFileName);
        if (!File.Exists(descriptorPath))
            throw new InvalidModException(
                $"У папці «{rootPath}» немає файлу {DescriptorFileName} — це не схоже на мод Hearts of Iron IV.");

        return new ModProject { RootPath = rootPath, Descriptor = ModDescriptor.Load(descriptorPath) };
    }
}

public sealed class InvalidModException(string message) : Exception(message);
