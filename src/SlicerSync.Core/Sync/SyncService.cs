using SlicerSync.Core.Config;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Setup;

namespace SlicerSync.Core.Sync;

public enum SyncOutcome
{
    /// <summary>Etwas wurde committet, geholt oder hochgeladen.</summary>
    Success,
    /// <summary>Alles war bereits aktuell.</summary>
    NoChanges,
    /// <summary>Pull-Konflikt: Rebase abgebrochen, nichts überschrieben.</summary>
    Conflict,
    Failed,
    /// <summary>Es lief bereits ein Sync.</summary>
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
/// Ablauf: git add -A → Commit nur bei Änderungen → git pull --rebase → git push.
/// Bei einem Konflikt wird der Rebase abgebrochen; der lokale Commit bleibt erhalten.
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
            return new SyncResult { Outcome = SyncOutcome.Busy, Message = "Synchronisierung läuft bereits." };

        SyncResult result;
        try
        {
            Started?.Invoke();
            result = SyncCore(_settings());
        }
        catch (Exception ex)
        {
            _log.Error("Synchronisierung fehlgeschlagen", ex);
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
    /// Führt eine Aktion aus, während garantiert kein Sync läuft (z. B. Ordner verschieben,
    /// Datei wiederherstellen). Wartet ggf. auf einen laufenden Sync.
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
            return new SyncResult { Outcome = SyncOutcome.Failed, Message = $"Kein Git-Repository unter {s.RepoPath}." };

        var warnings = CheckJunctions(s);
        foreach (var w in warnings)
            _log.Warn(w);

        if (repo.IsRebaseInProgress())
            return new SyncResult
            {
                Outcome = SyncOutcome.Failed,
                Message = "Im Repository läuft noch ein Rebase. Bitte manuell abschließen oder abbrechen (git rebase --abort).",
                Warnings = warnings,
            };

        repo.EnsureIdentity();

        // 1. Lokale Änderungen committen
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
                Message = staged.Count > 0 ? $"{staged.Count} Dateien lokal committet (kein Remote eingestellt)." : "Keine Änderungen.",
                FilesCommitted = staged.Count,
                Warnings = warnings,
            };
        }

        repo.SetRemote(s.RemoteUrl);

        // 2. Änderungen anderer PCs holen
        var pull = repo.PullRebase(s.Branch);
        if (pull == PullOutcome.Conflict)
        {
            const string msg = "Konflikt mit Änderungen eines anderen PCs. Der Abgleich wurde abgebrochen, lokal wurde nichts überschrieben. " +
                               "Bitte den Konflikt im Repository manuell lösen (siehe README).";
            _log.Error(msg);
            return new SyncResult { Outcome = SyncOutcome.Conflict, Message = msg, FilesCommitted = staged.Count, Warnings = warnings };
        }
        if (pull == PullOutcome.Updated)
            _log.Info("Änderungen vom Remote übernommen.");

        // 3. Hochladen, falls lokal etwas fehlt
        var pushed = false;
        var ahead = pull == PullOutcome.NoRemoteBranch ? null : repo.CommitsAhead(s.Branch);
        if (repo.HeadHash() != null && (ahead == null || ahead > 0))
        {
            repo.Push(s.Branch);
            pushed = true;
            _log.Info("Push erfolgreich.");
        }

        var changed = staged.Count > 0 || pull == PullOutcome.Updated || pushed;
        var parts = new List<string>();
        if (staged.Count > 0) parts.Add($"{staged.Count} Dateien gesichert");
        if (pull == PullOutcome.Updated) parts.Add("Änderungen von anderem PC übernommen");
        if (pushed && staged.Count == 0) parts.Add("ausstehende Commits hochgeladen");

        return new SyncResult
        {
            Outcome = changed ? SyncOutcome.Success : SyncOutcome.NoChanges,
            Message = changed ? string.Join(", ", parts) + "." : "Alles aktuell.",
            FilesCommitted = staged.Count,
            PulledChanges = pull == PullOutcome.Updated,
            Pushed = pushed,
            Warnings = warnings,
        };
    }

    /// <summary>Meldet Profilordner, deren Junction fehlt (z. B. weil ein Slicer-Update den Ordner neu angelegt hat).</summary>
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
                MappingState.NotLinked => $"{m.DisplayName}: Ordner ist nicht mehr verlinkt – Änderungen werden nicht gesichert. Bitte im Einrichtungsassistenten erneut einrichten.",
                MappingState.ForeignJunction => $"{m.DisplayName}: Junction zeigt auf einen anderen Ort.",
                _ => $"{m.DisplayName}: Ordner fehlt am Originalort ({m.OriginalPath}).",
            });
        }
        return warnings;
    }
}
