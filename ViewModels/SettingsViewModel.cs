using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _store;
    private readonly IPlatformService _platform;

    public SettingsViewModel(SettingsStore store, IPlatformService platform)
    {
        _store = store;
        _platform = platform;
        GamePath = store.Settings.GamePath ?? "";
        ModsDirectory = store.Settings.ModsDirectory ?? GameLocator.DefaultModsDirectory();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameStatus), nameof(IsGameFound))]
    public partial string GamePath { get; set; }

    [ObservableProperty]
    public partial string ModsDirectory { get; set; }

    public bool IsGameFound => GameLocator.IsGameDirectory(GamePath);

    public string GameStatus => IsGameFound
        ? GameLocator.ReadGameVersion(GamePath) is { } v ? $"Знайдено Hearts of Iron IV {v}" : "Знайдено Hearts of Iron IV"
        : "Гру не знайдено в цій папці";

    partial void OnGamePathChanged(string value)
    {
        _store.Settings.GamePath = string.IsNullOrWhiteSpace(value) ? null : value;
        _store.Save();
    }

    partial void OnModsDirectoryChanged(string value)
    {
        _store.Settings.ModsDirectory = string.IsNullOrWhiteSpace(value) ? null : value;
        _store.Save();
    }

    [RelayCommand]
    private async Task BrowseGamePath()
    {
        if (await _platform.PickFolderAsync("Оберіть папку Hearts of Iron IV", GamePath) is { } path)
            GamePath = path;
    }

    [RelayCommand]
    private async Task BrowseModsDirectory()
    {
        if (await _platform.PickFolderAsync("Оберіть папку модів лаунчера", ModsDirectory) is { } path)
            ModsDirectory = path;
    }

    [RelayCommand]
    private void DetectGamePath()
    {
        if (GameLocator.FindGamePath() is { } path)
            GamePath = path;
    }
}
