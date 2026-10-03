using SlicerSync.Core.Config;
using SlicerSync.Core.Logging;

namespace SlicerSync.Core.Sync;

/// <summary>Calls the sync action depending on the mode: interval, daily time or after a file change.</summary>
public sealed class SyncScheduler : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly Action _sync;
    private readonly ILogger _log;
    private readonly object _lock = new();
    private Timer? _timer;
    private FileSystemWatcher? _watcher;
    private Debouncer? _debouncer;
    private SyncMode _mode;
    private TimeSpan _interval;
    private TimeSpan _dailyTime;
    private DateTime _nextRun;

    public SyncScheduler(Action sync, ILogger log)
    {
        _sync = sync;
        _log = log;
    }

    public DateTime? NextRun { get { lock (_lock) return _mode is SyncMode.Interval or SyncMode.Daily ? _nextRun : null; } }

    public void Apply(AppSettings s)
    {
        lock (_lock)
        {
            StopCore();
            _mode = s.SyncMode;
            _interval = TimeSpan.FromMinutes(Math.Max(1, s.IntervalMinutes));
            _dailyTime = s.GetDailyTime();

            switch (_mode)
            {
                case SyncMode.Interval:
                    _nextRun = DateTime.Now + _interval;
                    _timer = new Timer(_ => OnTick(), null, Tick, Tick);
                    break;
                case SyncMode.Daily:
                    _nextRun = NextDailyRun(DateTime.Now, _dailyTime);
                    _timer = new Timer(_ => OnTick(), null, Tick, Tick);
                    break;
                case SyncMode.OnChange:
                    StartWatcher(s.RepoPath, TimeSpan.FromSeconds(Math.Max(1, s.DebounceSeconds)));
                    break;
            }
        }
        _log.Info(Loc.T($"Sync mode: {s.SyncMode}", $"Sync-Modus: {s.SyncMode}"));
    }

    /// <summary>Next time for the daily sync (today if not yet passed, otherwise tomorrow).</summary>
    public static DateTime NextDailyRun(DateTime now, TimeSpan time)
    {
        var today = now.Date + time;
        return today > now ? today : today.AddDays(1);
    }

    /// <summary>Whether a path in the repository should trigger a sync (.git internals don't).</summary>
    public static bool IsRelevantChange(string repoPath, string fullPath)
    {
        var rel = Path.GetRelativePath(repoPath, fullPath);
        var first = rel.Split('\\', '/')[0];
        return !first.Equals(".git", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("..", StringComparison.Ordinal);
    }

    private void OnTick()
    {
        bool due;
        lock (_lock)
        {
            due = DateTime.Now >= _nextRun;
            if (due)
                _nextRun = _mode == SyncMode.Daily ? NextDailyRun(DateTime.Now, _dailyTime) : DateTime.Now + _interval;
        }
        if (due)
            RunSafe();
    }

    private void StartWatcher(string repoPath, TimeSpan debounce)
    {
        if (!Directory.Exists(repoPath))
        {
            _log.Warn(Loc.T($"Cannot watch for changes, folder missing: {repoPath}", $"Dateiüberwachung nicht möglich, Ordner fehlt: {repoPath}"));
            return;
        }

        _debouncer = new Debouncer(debounce, RunSafe);
        _watcher = new FileSystemWatcher(repoPath)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        FileSystemEventHandler onChange = (_, e) =>
        {
            if (IsRelevantChange(repoPath, e.FullPath))
                _debouncer?.Trigger();
        };
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Deleted += onChange;
        _watcher.Renamed += (_, e) => onChange(_, e);
        _watcher.Error += (_, e) =>
        {
            _log.Warn(Loc.T("File watcher buffer overflow, triggering a sync.", "Dateiüberwachung: Puffer übergelaufen, Sync wird ausgelöst."), e.GetException());
            _debouncer?.Trigger();
        };
        _watcher.EnableRaisingEvents = true;
    }

    private void RunSafe()
    {
        try { _sync(); }
        catch (Exception ex) { _log.Error(Loc.T("Scheduled sync failed", "Geplante Synchronisierung fehlgeschlagen"), ex); }
    }

    private void StopCore()
    {
        _timer?.Dispose();
        _timer = null;
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _debouncer?.Dispose();
        _debouncer = null;
    }

    public void Dispose()
    {
        lock (_lock) StopCore();
    }
}

/// <summary>Runs an action only once no further trigger arrived for <c>delay</c>.</summary>
public sealed class Debouncer : IDisposable
{
    private readonly Timer _timer;
    private readonly TimeSpan _delay;
    private bool _disposed;

    public Debouncer(TimeSpan delay, Action action)
    {
        _delay = delay;
        _timer = new Timer(_ => { if (!_disposed) action(); }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Trigger()
    {
        if (!_disposed)
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
