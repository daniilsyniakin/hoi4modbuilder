using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels;
using HoI4ModBuilder.Views;

namespace HoI4ModBuilder;

public partial class App : Application, IWorkspaceHost
{
    private SettingsStore _settings = null!;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Window? _currentWindow;
    private UpdateService _updates = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _settings = SettingsStore.Load();
            _updates = new UpdateService(UpdateService.RepositoryUrl);
            _ = _updates.CheckAsync(manual: false);
            if (!TryOpenFromCommandLine(desktop.Args))
                ShowWelcome();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void OpenMod(ModProject mod)
    {
        _settings.AddRecent(mod.Name, mod.RootPath);
        var window = new MainWindow();
        window.DataContext = new MainWindowViewModel(mod, _settings, new PlatformService(window, _settings), this, _updates);
        SwitchTo(window);
    }

    public void ShowWelcome(WelcomePage page = WelcomePage.Mods)
    {
        var window = new WelcomeWindow();
        window.DataContext = new WelcomeViewModel(_settings, new PlatformService(window, _settings), this, page) { Updates = _updates };
        SwitchTo(window);
    }

    public void Exit()
    {
        _currentWindow?.Close();
        _desktop?.Shutdown();
    }

    /// <summary>Supports <c>HoI4ModBuilder &lt;mod folder&gt; [files to open…]</c>.</summary>
    private bool TryOpenFromCommandLine(string[]? args)
    {
        if (args is not [var path, .. var files])
            return false;
        try
        {
            OpenMod(ModProject.Load(path));
            if (_currentWindow?.DataContext is MainWindowViewModel workspace)
            {
                foreach (var file in files)
                    workspace.OpenFile(Path.GetFullPath(file));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidModException)
        {
            return false;
        }
    }

    // Show the new window before closing the old one, otherwise the lifetime sees
    // "last window closed" and shuts the app down.
    private void SwitchTo(Window window)
    {
        var previous = _currentWindow;
        _currentWindow = window;
        if (_desktop is not null)
            _desktop.MainWindow = window;
        window.Show();
        previous?.Close();
    }
}
