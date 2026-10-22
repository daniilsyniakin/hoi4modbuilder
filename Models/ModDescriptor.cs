using System.Text;
using System.Text.RegularExpressions;

namespace HoI4ModBuilder.Models;

/// <summary>
/// Contents of a <c>descriptor.mod</c> / <c>&lt;name&gt;.mod</c> file.
/// Only the keys the launcher cares about are handled; a full Clausewitz parser comes later.
/// </summary>
public sealed partial class ModDescriptor
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string? SupportedVersion { get; set; }
    public string? Picture { get; set; }
    public string? RemoteFileId { get; set; }
    public List<string> Tags { get; set; } = [];

    public static ModDescriptor Load(string filePath) => Parse(File.ReadAllText(filePath));

    public static ModDescriptor Parse(string text)
    {
        var descriptor = new ModDescriptor();

        foreach (Match match in ScalarRegex().Matches(text))
        {
            var value = match.Groups["value"].Value;
            switch (match.Groups["key"].Value)
            {
                case "name": descriptor.Name = value; break;
                case "version": descriptor.Version = value; break;
                case "supported_version": descriptor.SupportedVersion = value; break;
                case "picture": descriptor.Picture = value; break;
                case "remote_file_id": descriptor.RemoteFileId = value; break;
            }
        }

        var tagsBlock = TagsBlockRegex().Match(text);
        if (tagsBlock.Success)
        {
            descriptor.Tags = QuotedRegex().Matches(tagsBlock.Groups["body"].Value)
                .Select(m => m.Groups[1].Value)
                .ToList();
        }

        return descriptor;
    }

    /// <param name="path">
    /// Absolute mod path; only written to the outer <c>&lt;name&gt;.mod</c> file in the launcher's mod directory.
    /// </param>
    public string Serialize(string? path = null)
    {
        var sb = new StringBuilder();
        sb.Append("version=\"").Append(Version).Append("\"\n");
        if (Tags.Count > 0)
        {
            sb.Append("tags={\n");
            foreach (var tag in Tags)
                sb.Append("\t\"").Append(tag).Append("\"\n");
            sb.Append("}\n");
        }
        sb.Append("name=\"").Append(Name).Append("\"\n");
        if (!string.IsNullOrEmpty(Picture))
            sb.Append("picture=\"").Append(Picture).Append("\"\n");
        if (!string.IsNullOrEmpty(SupportedVersion))
            sb.Append("supported_version=\"").Append(SupportedVersion).Append("\"\n");
        if (!string.IsNullOrEmpty(path))
            sb.Append("path=\"").Append(path.Replace('\\', '/')).Append("\"\n");
        if (!string.IsNullOrEmpty(RemoteFileId))
            sb.Append("remote_file_id=\"").Append(RemoteFileId).Append("\"\n");
        return sb.ToString();
    }

    [GeneratedRegex("""^\s*(?<key>\w+)\s*=\s*"(?<value>[^"]*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex ScalarRegex();

    [GeneratedRegex("""\btags\s*=\s*\{(?<body>[^}]*)\}""")]
    private static partial Regex TagsBlockRegex();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedRegex();
}
