using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;

namespace SlicerSync.Core.History;

/// <summary>Verlauf anzeigen und einzelne Profildateien auf einen älteren Stand zurücksetzen.</summary>
public sealed class HistoryService
{
    private readonly Func<AppSettings> _settings;
    private readonly GitRunner _git;
    private readonly BackupService _backups;
    private readonly ILogger _log;

    public HistoryService(Func<AppSettings> settings, GitRunner git, BackupService backups, ILogger log)
    {
        _settings = settings;
        _git = git;
        _backups = backups;
        _log = log;
    }

    private GitRepository Repo => new(_git, _settings().RepoPath);

    public IReadOnlyList<CommitInfo> GetCommits(int maxCount = 100) => Repo.Log(maxCount);

    /// <summary>
    /// Setzt eine Datei auf den Stand nach <paramref name="commit"/> zurück. Wurde sie in diesem
    /// Commit gelöscht, wird der Stand davor wiederhergestellt. Die aktuelle Datei wird vorher gesichert.
    /// Gibt den Pfad der Sicherung zurück (null, wenn es die Datei aktuell nicht gibt).
    /// </summary>
    public string? RestoreFile(string commit, string repoRelativePath)
    {
        var repo = Repo;
        var source = commit;
        if (!repo.FileExistsInCommit(source, repoRelativePath))
        {
            source = commit + "~1";
            if (!repo.FileExistsInCommit(source, repoRelativePath))
                throw new InvalidOperationException($"Die Datei {repoRelativePath} ist in diesem Commit nicht vorhanden.");
        }

        var fullPath = Path.Combine(repo.Path, repoRelativePath.Replace('/', '\\'));
        string? backup = null;
        if (File.Exists(fullPath))
            backup = _backups.CreateBackup("datei-wiederherstellen", new[] { (fullPath, repoRelativePath) });

        repo.RestoreFile(source, repoRelativePath);
        _log.Info($"Datei wiederhergestellt: {repoRelativePath} (Stand {source})");
        return backup;
    }
}
