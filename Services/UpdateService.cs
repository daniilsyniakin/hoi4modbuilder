using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace HoI4ModBuilder.Services;

public enum UpdateState
{
    /// <summary>Nothing to show: not checked yet, or an automatic check found nothing.</summary>
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Failed,
}

/// <summary>
/// Updates from the project's GitHub releases (built by Velopack). Only an installed copy can
/// update itself; a development build (dotnet run) never offers updates.
/// </summary>
public sealed partial class UpdateService : ObservableObject
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _update;

    public UpdateService(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            return;
        try
        {
            var manager = new UpdateManager(new GithubSource(repositoryUrl, accessToken: null, prerelease: false));
            if (manager.IsInstalled)
                _manager = manager;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Updates unavailable: {ex.Message}");
        }
    }

    /// <summary>The repository the release build was made from (set by the release workflow).</summary>
    public static string? RepositoryUrl =>
        typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;

    /// <summary>Whether this copy was installed and can update itself.</summary>
    public bool IsSupported => _manager is not null;

    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsVisible), nameof(CanInstall))]
    public partial UpdateState State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? NewVersion { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int Progress { get; private set; }

    /// <summary>What the status bar says about updates.</summary>
    public string StatusText => State switch
    {
        UpdateState.Checking => "Перевірка оновлень…",
        UpdateState.UpToDate => $"Версія {CurrentVersion} найновіша",
        UpdateState.Available => $"Оновлення {NewVersion}",
        UpdateState.Downloading => $"Оновлення {NewVersion}: {Progress}%",
        UpdateState.Failed => "Не вдалося перевірити оновлення",
        _ => "",
    };

    public bool IsVisible => State != UpdateState.Idle;

    public bool CanInstall => State == UpdateState.Available;

    /// <param name="manual">A check the user asked for also reports "up to date" and failures.</param>
    public async Task CheckAsync(bool manual)
    {
        if (_manager is null || State is UpdateState.Checking or UpdateState.Downloading)
            return;
        if (manual)
            State = UpdateState.Checking;
        try
        {
            _update = await _manager.CheckForUpdatesAsync();
            NewVersion = _update?.TargetFullRelease.Version.ToString();
            State = _update is not null ? UpdateState.Available : manual ? UpdateState.UpToDate : UpdateState.Idle;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Update check failed: {ex.Message}");
            State = manual ? UpdateState.Failed : UpdateState.Idle;
        }
    }

    /// <summary>Downloads the update, lets the app save its work, then restarts into the new version.</summary>
    public async Task InstallAsync(Action beforeRestart)
    {
        if (_manager is null || _update is null || State != UpdateState.Available)
            return;
        State = UpdateState.Downloading;
        try
        {
            await _manager.DownloadUpdatesAsync(_update, percent => Dispatcher.UIThread.Post(() => Progress = percent));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Update download failed: {ex.Message}");
            State = UpdateState.Failed;
            return;
        }
        beforeRestart();
        _manager.ApplyUpdatesAndRestart(_update.TargetFullRelease);
    }
}
