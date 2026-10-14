using SlicerSync.Core.Config;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Setup;

namespace SlicerSync.Core.Sync;

public enum SyncOutcome
{
    /// <summary>Something was committed, pulled or pushed.</summary>
    Success,
    /// <summary>Everything was up to date already.</summary>
    NoChanges,
    /// <summary>Pull conflict: rebase aborted, nothing overwritten.</summary>
    Conflict,
    Failed,
    /// <summary>A sync was already running.</summary>
    Busy,
}

public sealed class SyncResult
{
    public SyncOutcome Outcome { get; init; }
    public string Message { get; init; } = "";
    public int FilesCommitted { get; init; }
    public bool PulledChanges { get; init; }
    public bool Pushed { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool IsProblem => Outcome is SyncOutcome.Conflict or SyncOutcome.Failed || Warnings.Count > 0;
}

/// <summary>
/// Flow: git add -A → commit only if something changed → git pull --rebase → git push.
/// On a conflict the rebase is aborted; the local commit is kept.
/// </summary>
public sealed class SyncService
{
    private readonly Func<AppSettings> _settings;
    private readonly GitRunner _git;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncService(Func<AppSettings> settings, GitRunner git, ILogger log)
    {
        _settings = settings;
        _git = git;
        _log = log;
    }

    public event Action? Started;
    public event Action<SyncResult>? Completed;

    public bool IsRunning => _gate.CurrentCount == 0;

    public SyncResult Sync()
    {
        if (!_gate.Wait(0))
            return new SyncResult { Outcome = SyncOutcome.Busy, Message = Loc.T("A sync is already running.", "Synchronisierung läuft bereits.") };

        SyncResult result;
        try
        {
            Started?.Invoke();
            result = SyncCore(_settings());
        }
        catch (Exception ex)
        {
            _log.Error(Loc.T("Sync failed", "Synchronisierung fehlgeschlagen"), ex);
            result = new SyncResult { Outcome = SyncOutcome.Failed, Message = ex.Message };
        }
        finally
        {
            _gate.Release();
        }

        Completed?.Invoke(result);
        return result;
    }

    /// <summary>
    /// Runs an action while it is guaranteed that no sync is running (e.g. moving folders,
    /// restoring a file). Waits for a running sync if necessary.
    /// </summary>
    public T RunExclusive<T>(Func<T> action)
    {
        _gate.Wait();
        try { return action(); }
        finally { _gate.Release(); }
    }

    private SyncResult SyncCore(AppSettings s)
    {
        var repo = new GitRepository(_git, s.RepoPath);
        if (!repo.Exists)
            return new SyncResult
            {
                Outcome = SyncOutcome.Failed,
                Message = Loc.T($"No Git repository at {s.RepoPath}.", $"Kein Git-Repository unter {s.RepoPath}."),
            };

        var warnings = CheckJunctions(s);
        foreach (var w in warnings)
            _log.Warn(w);

        if (repo.IsRebaseInProgress())
            return new SyncResult
            {
                Outcome = SyncOutcome.Failed,
                Message = Loc.T(
                    "A rebase is still in progress in the repository. Please finish or abort it manually (git rebase --abort).",
                    "Im Repository läuft noch ein Rebase. Bitte manuell abschließen oder abbrechen (git rebase --abort)."),
                Warnings = warnings,
            };

        repo.EnsureIdentity();

        // 1. Commit local changes
        repo.StageAll();
        var staged = repo.StagedFiles();
        if (staged.Count > 0)
        {
            var message = CommitMessage.Format(s.CommitMessageTemplate, DateTime.Now, Environment.MachineName, Environment.UserName, staged.Count);
            repo.Commit(message);
            _log.Info($"Commit: {message}");
        }

        var hasRemote = !string.IsNullOrWhiteSpace(s.RemoteUrl);
        if (!hasRemote)
        {
            return new SyncResult
            {
                Outcome = staged.Count > 0 ? SyncOutcome.Success : SyncOutcome.NoChanges,
                Message = staged.Count > 0
                    ? Loc.T($"{staged.Count} files committed locally (no remote configured).",
                            $"{staged.Count} Dateien lokal committet (kein Remote eingestellt).")
                    : Loc.T("No changes.", "Keine Änderungen."),
                FilesCommitted = staged.Count,
                Warnings = warnings,
            };
        }

        repo.SetRemote(s.RemoteUrl);

        // 2. Fetch changes from other PCs
        var pull = repo.PullRebase(s.Branch);
        if (pull == PullOutcome.Conflict)
        {
            var msg = Loc.T(
                "Conflict with changes from another PC. The sync was aborted and nothing was overwritten locally. " +
                "Please resolve the conflict in the repository manually (see README).",
                "Konflikt mit Änderungen eines anderen PCs. Der Abgleich wurde abgebrochen, lokal wurde nichts überschrieben. " +
                "Bitte den Konflikt im Repository manuell lösen (siehe README).");
            _log.Error(msg);
            return new SyncResult { Outcome = SyncOutcome.Conflict, Message = msg, FilesCommitted = staged.Count, Warnings = warnings };
        }
        if (pull == PullOutcome.Updated)
            _log.Info(Loc.T("Changes from the remote applied.", "Änderungen vom Remote übernommen."));

        // 3. Push if the remote is missing something
        var pushed = false;
        var ahead = pull == PullOutcome.NoRemoteBranch ? null : repo.CommitsAhead(s.Branch);
        if (repo.HeadHash() != null && (ahead == null || ahead > 0))
        {
            repo.Push(s.Branch);
            pushed = true;
            _log.Info(Loc.T("Push successful.", "Push erfolgreich."));
        }

        var changed = staged.Count > 0 || pull == PullOutcome.Updated || pushed;
        var parts = new List<string>();
        if (staged.Count > 0) parts.Add(Loc.T($"{staged.Count} files backed up", $"{staged.Count} Dateien gesichert"));
        if (pull == PullOutcome.Updated) parts.Add(Loc.T("changes from another PC applied", "Änderungen von anderem PC übernommen"));
        if (pushed && staged.Count == 0) parts.Add(Loc.T("pending commits uploaded", "ausstehende Commits hochgeladen"));

        return new SyncResult
        {
            Outcome = changed ? SyncOutcome.Success : SyncOutcome.NoChanges,
            Message = changed ? Capitalize(string.Join(", ", parts)) + "." : Loc.T("Everything up to date.", "Alles aktuell."),
            FilesCommitted = staged.Count,
            PulledChanges = pull == PullOutcome.Updated,
            Pushed = pushed,
            Warnings = warnings,
        };
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Reports profile folders whose junction is gone (e.g. a slicer update recreated the folder).</summary>
    public static List<string> CheckJunctions(AppSettings s)
    {
        var warnings = new List<string>();
        foreach (var m in s.EnabledMappings)
        {
            var state = SetupService.GetState(m, s.RepoPath);
            if (state == MappingState.Linked)
                continue;
            warnings.Add(state switch
            {
                MappingState.NotLinked => Loc.T(
                    $"{m.DisplayName}: the folder is no longer linked – changes are not backed up. " +
                    "Check it again in Settings → Profiles to back it up, or leave it unchecked and click OK to remove it.",
                    $"{m.DisplayName}: Ordner ist nicht mehr verlinkt – Änderungen werden nicht gesichert. " +
                    "In Einstellungen → Profile wieder anhaken, um ihn zu sichern, oder abgehakt lassen und mit OK entfernen."),
                MappingState.ForeignJunction => Loc.T(
                    $"{m.DisplayName}: the junction points to a different location.",
                    $"{m.DisplayName}: Junction zeigt auf einen anderen Ort."),
                _ => Loc.T(
                    $"{m.DisplayName}: the folder is missing at its original location ({m.OriginalPath}).",
                    $"{m.DisplayName}: Ordner fehlt am Originalort ({m.OriginalPath})."),
            });
        }
        return warnings;
    }
}
