using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Oriel.UI.Services;

public enum UpdateState { Idle, Checking, UpToDate, Downloading, ReadyToRestart, Failed }

/// <summary>
/// Self-updates from GitHub Releases (Velopack). Checks when Oriel opens, after every new wallpaper, and once a day,
/// downloads in the background, and installs when the user clicks Restart or when Oriel quits.
/// A no-op unless Oriel was installed as the Velopack AppImage (not under dotnet run or a copied folder).
/// </summary>
public sealed class UpdateService(ILogger<UpdateService> log) : IDisposable
{
    /// <summary>Public releases-only repo; the source repo is private, and updates must be readable without a token.</summary>
    public const string RepoUrl = "https://github.com/oriel-releases/linux";
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, prerelease: false));
    readonly SemaphoreSlim _gate = new(1, 1);
    Timer? _timer;
    UpdateInfo? _pending;

    /// <summary>Raised on a background thread.</summary>
    public event EventHandler? StateChanged;

    public UpdateState State { get; private set; }
    /// <summary>Download progress, 0 to 100.</summary>
    public int Progress { get; private set; }
    public string? AvailableVersion => _pending?.TargetFullRelease.Version.ToString();
    public bool IsInstalled => _manager.IsInstalled;

    public void Start()
    {
        if (!IsInstalled) return;
        _timer = new Timer(_ => _ = CheckAsync(), null, TimeSpan.Zero, CheckInterval);
    }

    public async Task CheckAsync()
    {
        if (!IsInstalled || State == UpdateState.ReadyToRestart || !await _gate.WaitAsync(0)) return;
        try
        {
            Set(UpdateState.Checking);
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Set(UpdateState.UpToDate);
                return;
            }
            _pending = update;
            Progress = 0;
            Set(UpdateState.Downloading);
            await _manager.DownloadUpdatesAsync(update, p =>
            {
                if (p == Progress) return;
                Progress = p;
                StateChanged?.Invoke(this, EventArgs.Empty);
            });
            log.LogInformation("Update {Version} downloaded", AvailableVersion);
            Set(UpdateState.ReadyToRestart);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Update check failed");
            _pending = null;
            Set(UpdateState.Failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Installs the downloaded update now and reopens Oriel. Exits this process.</summary>
    public void RestartToUpdate()
    {
        if (State == UpdateState.ReadyToRestart) _manager.ApplyUpdatesAndRestart(_pending!.TargetFullRelease);
    }

    /// <summary>On quit: install the downloaded update once this process has exited, without reopening.</summary>
    public void ApplyOnExit()
    {
        if (State != UpdateState.ReadyToRestart) return;
        try
        {
            _manager.WaitExitThenApplyUpdates(_pending!.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't schedule the update; it installs on next start instead");
        }
    }

    void Set(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer?.Dispose();
}
