using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HoI4ModBuilder.ViewModels;
using HoI4ModBuilder.Views;
using HoI4ModBuilder.Views.Dialogs;

namespace HoI4ModBuilder.Services;

/// <summary>OS and dialog interaction the view models need without depending on views.</summary>
public interface IPlatformService
{
    Task<string?> PickFolderAsync(string title, string? startPath = null);
    void RevealInFileManager(string path);
    Task ShowSettingsAsync();

    /// <param name="validate">Returns an error message, or null when the value is acceptable.</param>
    /// <param name="initialValue">Pre-filled and selected, e.g. the current name when renaming.</param>
    /// <returns>The entered text, or null if cancelled.</returns>
    Task<string?> PromptAsync(string title, string label, string confirmText, string? placeholder, Func<string, string?> validate,
        string? initialValue = null);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive);
}

public sealed class PlatformService(Window owner, SettingsStore settings) : IPlatformService
{
    public async Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        var storage = owner.StorageProvider;
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(startPath);

        var result = await storage.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public void RevealInFileManager(string path)
    {
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            // For files, open the containing folder with the file selected.
            info = File.Exists(path)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                : new ProcessStartInfo("explorer.exe", [path]);
        }
        else
        {
            var target = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
            info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [target]);
        }

        try
        {
            info.UseShellExecute = false;
            Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Failed to open file manager: {ex.Message}");
        }
    }

    public Task ShowSettingsAsync()
    {
        var window = new SettingsWindow { DataContext = new SettingsViewModel(settings, this) };
        return window.ShowDialog(owner);
    }

    public Task<string?> PromptAsync(string title, string label, string confirmText, string? placeholder, Func<string, string?> validate,
        string? initialValue = null) =>
        new PromptDialog(title, label, confirmText, placeholder, validate, initialValue).ShowDialog<string?>(owner);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive) =>
        new ConfirmDialog(title, message, confirmText, destructive).ShowDialog<bool>(owner);
}
