using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using SlicerSync.Core;
using SlicerSync.Core.Updates;

namespace SlicerSync.App;

/// <summary>
/// Checks for a newer release on start and once a day, and installs it: the installed version runs the
/// new installer silently (Windows asks for permission once), the portable version replaces its EXE.
/// </summary>
public sealed class Updater : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly AppServices _services;
    private readonly UpdateChecker _checker = new();
    private readonly SemaphoreSlim _busy = new(1, 1);
    private System.Threading.Timer? _timer;

    public Updater(AppServices services) => _services = services;

    /// <summary>A newer version that was found but not installed yet.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>Raised when <see cref="Available"/> changes or an update is about to be installed.</summary>
    public event Action? StateChanged;

    /// <summary>Raised with a short message for the user (balloon tip): text, isError.</summary>
    public event Action<string, bool>? Notify;

    /// <summary>Raised when the app must exit so the update can be installed; argument: EXE to start afterwards (portable) or null.</summary>
    public event Action<string?>? ExitRequested;

    public static Version CurrentVersion =>
        UpdateChecker.ParseVersion(Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new Version(0, 0, 0);

    /// <summary>The installer leaves its uninstaller next to the EXE; the portable version has none.</summary>
    public static bool IsPortable =>
        Environment.ProcessPath is not { } exe || !File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "unins000.exe"));

    public void Start()
    {
        if (Environment.ProcessPath is { } exe)
            UpdateChecker.CleanupOldExe(exe);
#if !DEBUG
        _timer = new System.Threading.Timer(_ => _ = CheckAsync(manual: false), null, FirstCheckDelay, CheckInterval);
#endif
    }

    /// <summary>Checks for a newer version; with <paramref name="manual"/> the result is always reported.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (!manual && !_services.Settings.CheckForUpdates)
            return;
        if (!await _busy.WaitAsync(0))
            return;
        try
        {
            var latest = await _checker.GetLatestAsync();
            if (latest == null || latest.Version <= CurrentVersion)
            {
                Available = null;
                StateChanged?.Invoke();
                if (manual)
                    Notify?.Invoke(Loc.T($"SlicerSync {CurrentVersion} is up to date.", $"SlicerSync {CurrentVersion} ist aktuell."), false);
                return;
            }

            Available = latest;
            StateChanged?.Invoke();
            _services.Log.Info(Loc.T($"Update available: {latest.Tag}", $"Update verfügbar: {latest.Tag}"));
            if (!manual && _services.Settings.InstallUpdatesAutomatically)
                await InstallCoreAsync(latest);
            else
                Notify?.Invoke(Loc.T(
                    $"SlicerSync {latest.Version} is available. Install it via the tray menu.",
                    $"SlicerSync {latest.Version} ist verfügbar. Installieren über das Tray-Menü."), false);
        }
        catch (Exception ex)
        {
            _services.Log.Warn(Loc.T("Update check failed", "Update-Prüfung fehlgeschlagen"), ex);
            if (manual)
                Notify?.Invoke(ex.Message, true);
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Installs the update found before (tray menu).</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } update || !await _busy.WaitAsync(0))
            return;
        try
        {
            await InstallCoreAsync(update);
        }
        finally
        {
            _busy.Release();
        }
    }

    private async Task InstallCoreAsync(UpdateInfo update)
    {
        var portable = IsPortable;
        var asset = update.AssetFor(portable);
        if (asset == null)
        {
            _services.Log.Warn(Loc.T($"Release {update.Tag} has no matching download.", $"Release {update.Tag} enthält keinen passenden Download."));
            return;
        }

        try
        {
            _services.Log.Info(Loc.T($"Downloading {asset.Name} …", $"Lade {asset.Name} herunter …"));
            var file = await _checker.DownloadAsync(asset, Path.Combine(_services.Paths.DataDir, "updates"));

            // Never interrupt a running sync: wait for it, then install and exit.
            string? restart = null;
            _services.Exclusive(() =>
            {
                var exe = Environment.ProcessPath!;
                if (portable)
                {
                    UpdateChecker.ReplacePortableExe(exe, file);
                    restart = exe;
                }
                else
                {
                    // /SILENT shows only a progress window; the installer closes nothing else and starts
                    // SlicerSync again when it is done. Windows asks for permission (UAC) here.
                    Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
                    {
                        UseShellExecute = true,
                    });
                }
                return true;
            });

            _services.Log.Info(Loc.T($"Installing SlicerSync {update.Version} …", $"Installiere SlicerSync {update.Version} …"));
            ExitRequested?.Invoke(restart);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user declined the Windows permission prompt.
            _services.Log.Info(Loc.T("Update cancelled in the Windows permission prompt.", "Update in der Windows-Sicherheitsabfrage abgebrochen."));
            Notify?.Invoke(Loc.T(
                $"Update to {update.Version} cancelled. You can install it later via the tray menu.",
                $"Update auf {update.Version} abgebrochen. Du kannst es später über das Tray-Menü installieren."), false);
        }
        catch (Exception ex)
        {
            _services.Log.Error(Loc.T("Update failed", "Update fehlgeschlagen"), ex);
            Notify?.Invoke(Loc.T($"Update failed: {ex.Message}", $"Update fehlgeschlagen: {ex.Message}"), true);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
