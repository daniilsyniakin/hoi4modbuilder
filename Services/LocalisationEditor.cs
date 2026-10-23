using HoI4ModBuilder.Game.Localisation;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Writes display texts (focus names, event titles…) into the mod's localisation files, in the
/// language currently shown. Changes go through the workspace, so they can be undone.
/// </summary>
/// <param name="modName">The mod's name, or its folder name, for naming a new localisation file.</param>
public sealed class LocalisationEditor(ScriptWorkspace workspace, LocalisationService localisation, string modName)
{
    /// <summary>Whether texts can be edited: a language is chosen (not raw ids).</summary>
    public bool CanEdit => localisation.Language is not null;

    /// <summary>
    /// The text of <paramref name="key"/> as the user edits it (real line breaks, colour codes kept);
    /// null when there is none in this language — a text borrowed from another language doesn't count.
    /// </summary>
    public string? EditableText(string key) =>
        localisation.Index.Find(key) is { } stored && !localisation.Index.IsFallback(key) ? LocalisationFile.Unescape(stored) : null;

    /// <summary>A text from another language, shown dimmed in an empty field as what to translate.</summary>
    public string? FallbackText(string key) =>
        localisation.Index.IsFallback(key) ? localisation.Index.FindDisplay(key) : null;

    /// <summary>
    /// Sets the text of <paramref name="key"/>: in the mod file that has it, otherwise in the file of
    /// this language mirroring <paramref name="source"/> (the key's file in another language), otherwise
    /// added to the file holding most keys with the same prefix (e.g. <c>UKR_</c>), otherwise to a new file.
    /// </summary>
    /// <param name="language">The language to write; null is the one shown.</param>
    /// <returns>An error message, or null.</returns>
    public string? SetText(string key, string text, string? language = null, (string File, string Language)? source = null)
    {
        language ??= localisation.Language;
        if (language is null)
            return "Оберіть мову локалізації, щоб змінювати тексти.";

        using var action = workspace.Action($"Текст «{key}»");
        var files = ModFiles(language);
        foreach (var file in files)
        {
            if (workspace.ReadText(file) is { } content && LocalisationFile.FindValue(content, key) is not null)
                return Finish(workspace.Edit(file, (current, _) =>
                    LocalisationFile.SetValue(current, key, text) is { } edit ? [edit] : []), key, text, language);
        }

        if (source is { } from)
        {
            var mirror = LocalisationTable.Mirror(from.File, from.Language, language);
            return Finish(File.Exists(mirror) || workspace.ReadText(mirror) is not null
                ? workspace.Edit(mirror, (current, _) => [LocalisationFile.Append(current, key, text)])
                : workspace.CreateFile(mirror, LocalisationFile.NewFile(language, key, text), withByteOrderMark: true), key, text, language);
        }

        var prefix = Prefix(key);
        var target = files
            .Select(f => (File: f, Score: workspace.ReadText(f) is { } c ? LocalisationFile.CountKeysWithPrefix(c, prefix) : -1))
            .Where(f => f.Score >= 0)
            .OrderByDescending(f => f.Score)
            .Select(f => f.File)
            .FirstOrDefault();
        if (target is not null)
            return Finish(workspace.Edit(target, (current, _) => [LocalisationFile.Append(current, key, text)]), key, text, language);

        var name = ModNames.Slug(modName, workspace.RootPath);
        var path = Path.Combine(workspace.RootPath, "localisation", language, name + LocalisationFile.Suffix(language));
        return Finish(workspace.CreateFile(path, LocalisationFile.NewFile(language, key, text), withByteOrderMark: true), key, text, language);
    }

    private string? Finish(string? error, string key, string text, string language)
    {
        // Texts of the shown language appear at once; the others when their files are re-read.
        if (error is null && language == localisation.Language)
            localisation.Apply(key, LocalisationFile.Escape(text).Replace("\\\"", "\""));
        return error;
    }

    private List<string> ModFiles(string language)
    {
        var folder = Path.Combine(workspace.RootPath, "localisation");
        if (!Directory.Exists(folder))
            return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(folder, "*" + LocalisationFile.Suffix(language), options)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary><c>UKR_army_reform</c> → <c>UKR_</c>, <c>ukr.1.t</c> → <c>ukr.</c>: keys that likely belong together.</summary>
    private static string Prefix(string key)
    {
        var cut = key.IndexOfAny(['_', '.']);
        return cut > 0 ? key[..(cut + 1)] : key;
    }
}
