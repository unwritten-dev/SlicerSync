using SlicerSync.Core;
using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Git;
using SlicerSync.Core.History;
using SlicerSync.Core.Hosting;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;
using SlicerSync.Core.Sync;

namespace SlicerSync.App;

/// <summary>Holds all services of the core library together.</summary>
public sealed class AppServices
{
    private volatile AppSettings _settings;

    public AppServices()
    {
        Paths = AppPaths.FromEnvironment();
        Log = new FileLogger(Paths.LogFile);
        Store = new SettingsStore(Paths.SettingsFile, Log);
        _settings = Store.Load();
        Loc.SetLanguage(_settings.Language);
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

    /// <summary>Current settings. Only ever replaced as a whole (forms work on copies).</summary>
    public AppSettings Settings => _settings;

    public GitRunner? Git { get; private set; }
    public SyncService? Sync { get; private set; }

    public bool GitAvailable => Git != null;

    /// <summary>Raised when a new <see cref="SyncService"/> was created.</summary>
    public event Action? SyncReplaced;

    /// <summary>Looks for git.exe again (e.g. after the user installed Git).</summary>
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
            Log.Info(Loc.T($"Git found: {exe}", $"Git gefunden: {exe}"));
        }
        return true;
    }

    public void ApplySettings(AppSettings settings)
    {
        Store.Save(settings);
        _settings = settings;
        Autostart.Apply(settings.Autostart);
    }

    public SetupService CreateSetup() => new(RequireGit(), Backups, Guard, Detector, Log);

    public HistoryService CreateHistory() => new(() => _settings, RequireGit(), Backups, Log);

    public RemoteCreator CreateRemoteCreator() => new(RequireGit(), Log);

    /// <summary>Runs a file operation while it is guaranteed that no sync is running.</summary>
    public T Exclusive<T>(Func<T> action) => Sync != null ? Sync.RunExclusive(action) : action();

    private GitRunner RequireGit() =>
        Git ?? throw new InvalidOperationException(Loc.T(
            "Git was not found. Please install Git for Windows.",
            "Git wurde nicht gefunden. Bitte Git for Windows installieren."));
}
