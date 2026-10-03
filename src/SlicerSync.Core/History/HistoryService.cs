using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;

namespace SlicerSync.Core.History;

/// <summary>Shows the history and restores single profile files to an older state.</summary>
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
    /// Restores a file to its state after <paramref name="commit"/>. If the file was deleted in that
    /// commit, the state before is restored. The current file is backed up first.
    /// Returns the backup path (null if the file does not exist at the moment).
    /// </summary>
    public string? RestoreFile(string commit, string repoRelativePath)
    {
        var repo = Repo;
        var source = commit;
        if (!repo.FileExistsInCommit(source, repoRelativePath))
        {
            source = commit + "~1";
            if (!repo.FileExistsInCommit(source, repoRelativePath))
                throw new InvalidOperationException(Loc.T(
                    $"The file {repoRelativePath} does not exist in this commit.",
                    $"Die Datei {repoRelativePath} ist in diesem Commit nicht vorhanden."));
        }

        var fullPath = Path.Combine(repo.Path, repoRelativePath.Replace('/', '\\'));
        string? backup = null;
        if (File.Exists(fullPath))
            backup = _backups.CreateBackup("restore-file", new[] { (fullPath, repoRelativePath) });

        repo.RestoreFile(source, repoRelativePath);
        _log.Info(Loc.T($"File restored: {repoRelativePath} (state {source})", $"Datei wiederhergestellt: {repoRelativePath} (Stand {source})"));
        return backup;
    }
}
