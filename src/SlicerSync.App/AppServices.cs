using SlicerSync.Core;
using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Git;
using SlicerSync.Core.History;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;
using SlicerSync.Core.Sync;

namespace SlicerSync.App;

/// <summary>Hält alle Dienste der Kernbibliothek zusammen.</summary>
public sealed class AppServices
{
    private volatile AppSettings _settings;

    public AppServices()
    {
        Paths = AppPaths.FromEnvironment();
        Log = new FileLogger(Paths.LogFile);
        Store = new SettingsStore(Paths.SettingsFile, Log);
        _settings = Store.Load();
        Backups = new BackupService(Paths.BackupDir, Log);
        Guard = new ProcessGuard();
        Detector = new SlicerDetector(Paths);
        RefreshGit();
    }

    public AppPaths Paths { get; }
    public FileLogger Log { get; }
    public SettingsStore Store { get; }
    public BackupService Backups { get; }
    public ProcessGuard Guard { get; }
    public SlicerDetector Detector { get; }

    /// <summary>Aktuelle Einstellungen. Wird nur als Ganzes ersetzt (Formulare arbeiten auf Kopien).</summary>
    public AppSettings Settings => _settings;

    public GitRunner? Git { get; private set; }
    public SyncService? Sync { get; private set; }

    public bool GitAvailable => Git != null;

    /// <summary>Sucht git.exe erneut (z. B. nachdem der Nutzer Git installiert hat).</summary>
    public bool RefreshGit()
    {
        var exe = GitLocator.Find(_settings.GitPath);
        if (exe == null)
        {
            Git = null;
            Sync = null;
            return false;
        }
        if (Git?.GitExe != exe)
        {
            Git = new GitRunner(exe);
            Sync = new SyncService(() => _settings, Git, Log);
            SyncReplaced?.Invoke();
            Log.Info($"Git gefunden: {exe}");
        }
        return true;
    }

    /// <summary>Wird ausgelöst, wenn ein neuer <see cref="SyncService"/> erzeugt wurde.</summary>
    public event Action? SyncReplaced;

    public void ApplySettings(AppSettings settings)
    {
        Store.Save(settings);
        _settings = settings;
        Autostart.Apply(settings.Autostart);
    }

    public SetupService CreateSetup() => new(RequireGit(), Backups, Guard, Detector, Log);

    public HistoryService CreateHistory() => new(() => _settings, RequireGit(), Backups, Log);

    /// <summary>Führt eine Datei-Operation aus, während sicher kein Sync läuft.</summary>
    public T Exclusive<T>(Func<T> action) => Sync != null ? Sync.RunExclusive(action) : action();

    private GitRunner RequireGit() =>
        Git ?? throw new InvalidOperationException("Git wurde nicht gefunden. Bitte Git for Windows installieren.");
}
